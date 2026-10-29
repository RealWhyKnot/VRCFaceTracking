using System.Collections.Concurrent;
using System.CommandLine;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;

namespace VRCFaceTracking.ModuleProcess;

internal sealed partial class ModuleProcessMain
{
    private const double ConnectionTimeoutSeconds = 60.0;
    private const double ConnectedTimeoutSeconds = 10.0;
    private static volatile bool WaitForPackets = true;
    public static ModuleAssembly? DefModuleAssembly;
    public static ILoggerFactory LoggerFactory = NullLoggerFactory.Instance;
    public static ILogger<ModuleProcessMain> Logger = NullLogger<ModuleProcessMain>.Instance;
    public static VrcftSandboxClient? Client;
    public static CancellationTokenSource Cts = new();

    private static readonly LogLevelGate Gate = new();
    private static FileLoggerProvider? LogFile;
    private static readonly ConcurrentQueue<IpcPacket> PacketsToSend = new();
    private static Timer? ConnectionTimer;
    private static volatile bool Connected;
    private static Thread? UpdateThread;

    private static readonly AutoResetEvent Wakeup = new(initialState: false);
    private const int SendLoopFallbackWakeMs = 1000;
    private static ReplyUpdatePacket? Latest;
    private static volatile bool ImageStreamEnabled;
    private const int ImageFrameIntervalMs = 90;
    private static readonly Stopwatch EyeFrameTimer = Stopwatch.StartNew();
    private static readonly Stopwatch LipFrameTimer = Stopwatch.StartNew();

    public static int Main(string[] args)
    {
        string? modulePathArg = args.SkipWhile(a => !string.Equals(a, "--module-path", StringComparison.Ordinal)).Skip(1).FirstOrDefault();
        string moduleName = Path.GetFileNameWithoutExtension(modulePathArg ?? "module");
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        Gate.Set(args.Contains("--verbose") || BuildInfo.VerboseForced);

        string throttling = OptOutOfThrottling();
        var logFile = new FileLoggerProvider(LogDirectories.Current, LogFileNames.Module(moduleName, DateTime.Now), BuildInfo.HeaderBlock($"ModuleProcess {moduleName}", DateTimeOffset.UtcNow), Gate, TimeProvider.System);
        LogFile = logFile;
        logFile.WriteRaw($"verbose={Gate.Verbose} minimum={Gate.Minimum} priority={Process.GetCurrentProcess().PriorityClass} throttling={throttling} gc={GCSettings.LatencyMode}");

        ConfigureLogging(logFile);
        HookProcessEvents(logFile);

        try
        {
            if (args.Length < 1)
            {
                LogNoArgumentsSupplied(Logger);
                return ModuleProcessExitCodes.InvalidArgs;
            }

            return BuildRootCommand(logFile).Parse(args).Invoke(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
        }
        finally
        {
            Client?.Dispose();
            logFile.Flush();
        }
    }

    private static string OptOutOfThrottling()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "n/a";
        }

        string throttling = PowerThrottling.TryOptOut(out uint mask, out int error)
            ? $"0x{mask:X} {PowerThrottling.Describe()}"
            : $"failed (error {error})";
        _ = ProcessPriority.TryRaiseToAboveNormal();
        return throttling;
    }

    private static void ConfigureLogging(FileLoggerProvider logFile)
    {
        ServiceProvider serviceProvider = new ServiceCollection()
            .AddLogging(loggingBuilder => loggingBuilder
                .ClearProviders()
                .SetMinimumLevel(LogLevel.Trace)
                .AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)
                .AddFilter<ConsoleLoggerProvider>(category: null, LogLevel.Error)
                .AddProvider(logFile)
                .AddProvider(new ProxyLoggerProvider(Gate)))
            .BuildServiceProvider();

        LoggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        Logger = LoggerFactory.CreateLogger<ModuleProcessMain>();
    }

    private static void HookProcessEvents(FileLoggerProvider logFile)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashHandlers.ReportUnhandled(Logger, e, logFile.Flush);
        TaskScheduler.UnobservedTaskException += (_, e) => CrashHandlers.ReportUnobserved(Logger, e, logFile.Flush);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            LogProcessExitRequested(Logger);
            WaitForPackets = false;
            DefModuleAssembly?.UpdateCts?.Cancel();
            Cts.Cancel();
            Wakeup.Set();
            UpdateThread?.Join(TimeSpan.FromSeconds(5));
            logFile.Flush();
        };
    }

    private static RootCommand BuildRootCommand(FileLoggerProvider logFile)
    {
        var portOption = new Option<int?>("--port")
        {
            Description = "The UDP port the VRCFT server is running on.",
        };
        var modulePathOption = new Option<string?>("--module-path")
        {
            Description = "The path to the module to load.",
        };
        var parentPidOption = new Option<int?>("--parent-pid")
        {
            Description = "PID of the parent VRCFT process. Module process exits if parent dies.",
        };
        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Write Debug and Information level log lines.",
        };

        var rootCommand = new RootCommand("VRCFT Sandbox Module");
        rootCommand.Options.Add(portOption);
        rootCommand.Options.Add(modulePathOption);
        rootCommand.Options.Add(parentPidOption);
        rootCommand.Options.Add(verboseOption);

        rootCommand.SetAction(parseResult =>
        {
            string? modulePath = parseResult.GetValue(modulePathOption);
            int? port = parseResult.GetValue(portOption);
            int? parentPid = parseResult.GetValue(parentPidOption);
            if (modulePath == null || port is null or 0)
            {
                LogModulePathPortRequired(Logger);
                return ModuleProcessExitCodes.InvalidArgs;
            }

            return VrcftMain(modulePath, port.Value, parentPid, logFile.CreateLogger(nameof(ModuleProcessMain)));
        });

        return rootCommand;
    }

    private static int VrcftMain(string modulePath, int serverPortNumber, int? parentPid, ILogger fileLogger)
    {
        Thread.Sleep(50);

        if (parentPid.HasValue)
        {
            StartParentWatchdog(parentPid.Value);
        }
        else
        {
            LogNoParentPidProvidedLingering(Logger);
        }

        var client = new VrcftSandboxClient(serverPortNumber, LoggerFactory);
        Client = client;
        ProxyLogger.OnLog += (level, msg) => client.SendData(new EventLogPacket(level, msg));

        if (!TryLoadModule(modulePath, out ModuleAssembly? moduleAssembly, out ExtTrackingModule? trackingModule))
        {
            return ModuleProcessExitCodes.ModuleLoadFailed;
        }

        SeedSentinelTrackingData();

        client.OnPacketReceivedCallback += (in packet) => OnHostPacket(packet, trackingModule, moduleAssembly, client, fileLogger);
        client.Connect(modulePath);
        LogInitializing(Logger, trackingModule.GetType().Assembly.FullName ?? "");

        ConnectionTimer = new Timer(_ =>
        {
            LogNoPacketsHostShuttingModule(Logger, Connected ? ConnectedTimeoutSeconds : ConnectionTimeoutSeconds);
            LogFile?.Flush();
            Environment.Exit(ModuleProcessExitCodes.NetworkConnectionTimedOut);
        }, state: null, TimeSpan.FromSeconds(ConnectionTimeoutSeconds), Timeout.InfiniteTimeSpan);

        RunSendLoop(client);

        moduleAssembly.UpdateCts?.Cancel();
        UpdateThread?.Join(TimeSpan.FromSeconds(2));
        ConnectionTimer.Dispose();

        LogFile?.Flush();
        Environment.Exit(ModuleProcessExitCodes.OK);
        return ModuleProcessExitCodes.OK;
    }

    private static void StartParentWatchdog(int parentPid)
    {
        var watchdogThread = new Thread(() => WatchParent(parentPid))
        {
            IsBackground = true,
            Name = "ParentWatchdog",
        };
        watchdogThread.Start();
    }

    private static void WatchParent(int parentPid)
    {
        WaitForHostExit(parentPid);

        LogHostProcessExitedTearingDown(Logger, parentPid);
        var teardownThread = new Thread(() =>
        {
            DefModuleAssembly?.UpdateCts?.Cancel();
            DefModuleAssembly?.TrackingModule?.Teardown();
        })
        {
            IsBackground = true,
        };
        teardownThread.Start();
        teardownThread.Join(TimeSpan.FromSeconds(10));

        LogFile?.Flush();
        Environment.Exit(ModuleProcessExitCodes.ParentExited);
    }

    private static void WaitForHostExit(int pid)
    {
        Process parent;
        try
        {
            parent = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (parent)
        {
            parent.WaitForExit();
        }
    }

    private static bool TryLoadModule(string modulePath, [NotNullWhen(true)] out ModuleAssembly? moduleAssembly, [NotNullWhen(true)] out ExtTrackingModule? trackingModule)
    {
        trackingModule = null;
        try
        {
            moduleAssembly = new ModuleAssembly(Logger, LoggerFactory, modulePath);
        }
        catch (ArgumentException e)
        {
            LogInvalidModulePath(Logger, modulePath, e.Message);
            moduleAssembly = null;
            return false;
        }

        DefModuleAssembly = moduleAssembly;
        moduleAssembly.LoadAssembly();
        trackingModule = moduleAssembly.TrackingModule;
        if (trackingModule == null)
        {
            LogNoExtTrackingModuleImplementationCouldLoaded(Logger, modulePath);
            return false;
        }

        return true;
    }

    private static void SeedSentinelTrackingData()
    {
        UnifiedTracking.Data = new()
        {
            Eye = new()
            {
                Left = new()
                {
                    Gaze = new(0xFFFFFFFF, 0xFFFFFFFF),
                    Openness = 0xFFFFFFFF,
                    PupilDiameter_MM = 0xFFFFFFFF,
                },
                Right = new()
                {
                    Gaze = new(0xFFFFFFFF, 0xFFFFFFFF),
                    Openness = 0xFFFFFFFF,
                    PupilDiameter_MM = 0xFFFFFFFF,
                },
                _maxDilation = 0xFFFFFFFF,
                _minDilation = 0xFFFFFFFF,
            },
            Head = new()
            {
                HeadYaw = 0xFFFFFFFF,
                HeadPitch = 0xFFFFFFFF,
                HeadRoll = 0xFFFFFFFF,
                HeadPosX = 0xFFFFFFFF,
                HeadPosY = 0xFFFFFFFF,
                HeadPosZ = 0xFFFFFFFF,
            },
        };
        for (int i = 0; i < (int)UnifiedExpressions.Max + 1; i++)
        {
            UnifiedTracking.Data.Shapes[i].Weight = 0xFFFFFFFF;
        }
    }

    private static void RunSendLoop(VrcftSandboxClient client)
    {
        while (WaitForPackets && !Cts.IsCancellationRequested)
        {
            while (PacketsToSend.TryDequeue(out IpcPacket? pkt))
            {
                client.SendData(pkt);
            }

            if (PacketsToSend.IsEmpty)
            {
                Wakeup.WaitOne(SendLoopFallbackWakeMs);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Process exit requested")]
    private static partial void LogProcessExitRequested(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "No arguments supplied")]
    private static partial void LogNoArgumentsSupplied(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "--module-path and --port are required")]
    private static partial void LogModulePathPortRequired(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Host process {Pid} exited, tearing down module")]
    private static partial void LogHostProcessExitedTearingDown(ILogger logger, int pid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No parent pid provided. Lingering process detection is limited to existing timeouts and graceful shutdown")]
    private static partial void LogNoParentPidProvidedLingering(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Invalid module path {Path}: {Message}")]
    private static partial void LogInvalidModulePath(ILogger logger, string path, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "No ExtTrackingModule implementation could be loaded from {Path}")]
    private static partial void LogNoExtTrackingModuleImplementationCouldLoaded(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Initializing {Module}")]
    private static partial void LogInitializing(ILogger logger, string module);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No packets from the host for {Timeout}s, shutting the module down")]
    private static partial void LogNoPacketsHostShuttingModule(ILogger logger, double timeout);
}
