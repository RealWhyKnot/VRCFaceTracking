using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.IPC;

namespace VRCFaceTracking.ModuleProcess;

public class ModuleProcessMain
{
    private const double CONNECTION_TIMEOUT = 60.0;
    private static bool WaitForPackets = true;
    public static ModuleAssembly DefModuleAssembly;
    public static ILoggerFactory? LoggerFactory;
    public static ILogger<ModuleProcessMain> Logger;
    public static VrcftSandboxClient Client;
    public static CancellationTokenSource cts = new();

    private static readonly LogLevelGate Gate = new();
    private static FileLoggerProvider? _fileLogger;
    private static Queue<IpcPacket> _packetsToSend = new ();
    private static Timer? _connectionTimer;

    private static object _callbackLock = new ();
    private static bool _shouldCallReceive = false;
    public static void QueueReceiveEvent()
    {
        lock ( _callbackLock )
        {
            _shouldCallReceive = true;
        }
    }

    public static int Main(string[] args)
    {
        var modulePathArg = args.SkipWhile(a => a != "--module-path").Skip(1).FirstOrDefault();
        var moduleName = Path.GetFileNameWithoutExtension(modulePathArg ?? "module");
        Gate.Set(args.Contains("--verbose") || BuildInfo.VerboseForced);
        _fileLogger = new FileLoggerProvider(Core.Utils.LogDirectory, LogFileNames.Module(moduleName, DateTime.Now), BuildInfo.HeaderBlock($"ModuleProcess {moduleName}"), Gate);
        _fileLogger.WriteRaw($"verbose={Gate.Verbose} minimum={Gate.Minimum}");

        var serviceProvider = new ServiceCollection()
            .AddLogging(loggingBuilder => loggingBuilder
                .ClearProviders()
                .SetMinimumLevel(LogLevel.Trace)
                .AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)
                .AddFilter<ConsoleLoggerProvider>(null, LogLevel.Error)
                .AddProvider(_fileLogger)
                .AddProvider(new ProxyLoggerProvider(Gate)))
            .BuildServiceProvider();

        LoggerFactory = serviceProvider.GetService<ILoggerFactory>();
        Logger = LoggerFactory!.CreateLogger<ModuleProcessMain>();
        CrashHandlers.Install(Logger, _fileLogger.Flush);

        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            Logger.LogInformation("Process exit requested");
            WaitForPackets = false;
            DefModuleAssembly?._updateCts?.Cancel();
            cts.Cancel();
            cts.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
            _fileLogger.Flush();
        };

        try
        {
            if ( args.Length < 1 )
            {
                Logger.LogCritical("No arguments supplied");
                return ModuleProcessExitCodes.INVALID_ARGS;
            }

            var portOption = new Option<int?>("--port")
            {
                Description = "The UDP port the VRCFT server is running on."
            };
            var modulePathOption = new Option<string?>("--module-path")
            {
                Description = "The path to the module to load."
            };
            var parentPidOption = new Option<int?>("--parent-pid")
            {
                Description = "PID of the parent VRCFT process. Module process exits if parent dies."
            };
            var verboseOption = new Option<bool>("--verbose")
            {
                Description = "Write Debug and Information level log lines."
            };

            var rootCommand = new RootCommand("VRCFT Sandbox Module");
            rootCommand.Options.Add(portOption);
            rootCommand.Options.Add(modulePathOption);
            rootCommand.Options.Add(parentPidOption);
            rootCommand.Options.Add(verboseOption);

            rootCommand.SetAction(parseResult =>
            {
                var modulePath = parseResult.GetValue(modulePathOption);
                var port = parseResult.GetValue(portOption);
                var parentPid = parseResult.GetValue(parentPidOption);
                if (modulePath == null || port is null or 0)
                {
                    Logger.LogCritical("--module-path and --port are required");
                    return ModuleProcessExitCodes.INVALID_ARGS;
                }

                return VrcftMain(modulePath, port.Value, parentPid);
            });

            return rootCommand.Parse(args).Invoke();
        }
        catch ( Exception ex )
        {
            Logger.LogCritical(ex, "Module process crashed");
            return ModuleProcessExitCodes.EXCEPTION_CRASH;
        }
        finally
        {
            Client?.Dispose();
            _fileLogger.Flush();
        }
    }

    static int VrcftMain(string modulePath, int serverPortNumber, int? parentPid = null)
    {
        Thread.Sleep(50);

        if (parentPid.HasValue)
        {
            var watchdogThread = new Thread(() =>
            {
                try
                {
                    using var parent = Process.GetProcessById(parentPid.Value);
                    parent.WaitForExit();
                }
                catch (Exception) { }

                Logger.LogWarning("Host process {Pid} exited, tearing down module", parentPid.Value);
                var teardownThread = new Thread(() =>
                {
                    try
                    {
                        DefModuleAssembly?._updateCts?.Cancel();
                        DefModuleAssembly?.TrackingModule?.Teardown();
                    }
                    catch (Exception) { }
                });
                teardownThread.IsBackground = true;
                teardownThread.Start();
                teardownThread.Join(TimeSpan.FromSeconds(10));

                _fileLogger?.Flush();
                Environment.Exit(ModuleProcessExitCodes.PARENT_EXITED);
            });
            watchdogThread.IsBackground = true;
            watchdogThread.Name = "ParentWatchdog";
            watchdogThread.Start();
        }
        else
        {
            Logger.LogWarning("No parent pid provided. Lingering process detection is limited to existing timeouts and graceful shutdown");
        }

        Client = new VrcftSandboxClient(serverPortNumber, LoggerFactory);

        ProxyLogger.OnLog += (level, msg) =>
        {
            var pkt = new EventLogPacket(level, msg);
            Client.SendData(pkt);
        };

        DefModuleAssembly = new ModuleAssembly(Logger, LoggerFactory, modulePath);
        DefModuleAssembly.TryLoadAssembly();
        if (DefModuleAssembly.TrackingModule == null)
        {
            Logger.LogError("No ExtTrackingModule implementation could be loaded from {Path}", modulePath);
            return ModuleProcessExitCodes.MODULE_LOAD_FAILED;
        }

        UnifiedTracking.Data = new() {
            Eye = new()
            {
                Left = new()
                {
                    Gaze = new(0xFFFFFFFF, 0xFFFFFFFF),
                    Openness = 0xFFFFFFFF,
                    PupilDiameter_MM = 0xFFFFFFFF
                },
                Right = new()
                {
                    Gaze = new(0xFFFFFFFF, 0xFFFFFFFF),
                    Openness = 0xFFFFFFFF,
                    PupilDiameter_MM = 0xFFFFFFFF
                },
                _maxDilation = 0xFFFFFFFF,
                _minDilation = 0xFFFFFFFF,
            }
        };
        for ( int i = 0; i < ( int )UnifiedExpressions.Max + 1; i++ )
        {
            UnifiedTracking.Data.Shapes[i].Weight = 0xFFFFFFFF;
        }

        Client.OnReceiveShouldBeQueued += QueueReceiveEvent;
        Client.OnPacketReceivedCallback += (in IpcPacket packet) => {
            _connectionTimer?.Change(TimeSpan.FromSeconds(CONNECTION_TIMEOUT), Timeout.InfiniteTimeSpan);

            switch ( packet.GetPacketType() )
            {
                case IpcPacket.PacketType.EventGetSupported:
                    {
                        var result = DefModuleAssembly.TrackingModule.Supported;
                        var pkt = new ReplySupportedPacket()
                        {
                            eyeAvailable        = result.SupportsEye,
                            expressionAvailable = result.SupportsExpression
                        };
                        _packetsToSend.Enqueue(pkt);
                        break;
                    }
                case IpcPacket.PacketType.EventInit:
                    {
                        var pkt = (EventInitPacket) packet;

                        bool eyeSuccess, expressionSuccess;
                        try
                        {
                            (eyeSuccess, expressionSuccess) = DefModuleAssembly.TrackingModule.Initialize(pkt.eyeAvailable, pkt.expressionAvailable);
                        }
                        catch ( MissingMethodException )
                        {
                            Logger.LogError("{moduleName} does not properly implement ExtTrackingModule. Skipping.", DefModuleAssembly.TrackingModule.GetType().Name);
                            return;
                        } catch ( Exception e )
                        {
                            Logger.LogError(e, "Exception initializing {module}. Skipping.", DefModuleAssembly.TrackingModule.GetType().Name);
                            return;
                        }

                        DefModuleAssembly._updateCts = new CancellationTokenSource();
                        var thread = new Thread(() =>
                        {
                            try
                            {
                                while (!DefModuleAssembly._updateCts.IsCancellationRequested)
                                {
                                    DefModuleAssembly.TrackingModule.Update();
                                }
                            }
                            catch (Exception e)
                            {
                                Logger.LogCritical(e, "Module update loop crashed");
                                _fileLogger?.Flush();
                                throw;
                            }
                        });
                        thread.IsBackground = true;
                        thread.Start();

                        var pktNew = new ReplyInitPacket()
                        {
                            eyeSuccess              = eyeSuccess,
                            expressionSuccess       = expressionSuccess,
                            ModuleInformationName   = DefModuleAssembly.TrackingModule.ModuleInformation.Name,
                            IconDataStreams         = DefModuleAssembly.TrackingModule.ModuleInformation.StaticImages
                        };
                        _packetsToSend.Enqueue(pktNew);
                        break;
                    }

                case IpcPacket.PacketType.EventTeardown:
                    {
                        Logger.LogInformation("Received Teardown packet");
                        DefModuleAssembly._updateCts?.Cancel();
                        try
                        {
                            DefModuleAssembly.TrackingModule.Teardown();
                        }
                        catch(Exception e)
                        {
                            Logger.LogWarning(e, "Tracking module failed to cleanly shut down.");
                        }

                        Logger.LogInformation("Cancelled Update Threads");

                        var pkt = new ReplyTeardownPacket();
                        Client.SendData(pkt);

                        Logger.LogInformation("Sent teardown ACK");

                        _fileLogger?.Flush();
                        Environment.Exit(ModuleProcessExitCodes.OK);
                        break;
                    }

                case IpcPacket.PacketType.EventUpdate:
                    {
                        var pkt = new ReplyUpdatePacket();
                        _packetsToSend.Enqueue(pkt);
                        break;
                    }

                case IpcPacket.PacketType.EventUpdateStatus:
                    {
                        var pkt = (EventStatusUpdatePacket) packet;
                        DefModuleAssembly.TrackingModule.Status = pkt.ModuleState;

                        break;
                    }

                case IpcPacket.PacketType.EventSetVerbose:
                    {
                        var pkt = (EventSetVerbosePacket) packet;
                        Gate.Set(pkt.Verbose || BuildInfo.VerboseForced);
                        _fileLogger?.WriteRaw($"verbose={Gate.Verbose} minimum={Gate.Minimum}");
                        break;
                    }
            }

        };
        if (OperatingSystem.IsWindows())
        {
            Core.Utils.TimeBeginPeriod(1);
        }

        Client.Connect(modulePath);
        Logger.LogInformation("Initializing {module}", DefModuleAssembly.Assembly.ToString());

        _connectionTimer = new Timer(_ =>
        {
            Logger.LogWarning("No packets received for {timeout}s, assuming connection lost", CONNECTION_TIMEOUT);
            _fileLogger?.Flush();
            Environment.Exit(ModuleProcessExitCodes.NETWORK_CONNECTION_TIMED_OUT);
        }, null, TimeSpan.FromSeconds(CONNECTION_TIMEOUT), Timeout.InfiniteTimeSpan);

        while ( WaitForPackets && !cts.IsCancellationRequested)
        {
            while (_packetsToSend.TryDequeue(out IpcPacket pkt))
            {
                if (pkt == null) continue;
                Client.SendData(pkt);
            }

            if ( _shouldCallReceive )
            {
                Client.ReceivePackets();
            }

            Thread.Sleep(1);
        }

        DefModuleAssembly._updateCts?.Cancel();
        _connectionTimer?.Dispose();

        if (OperatingSystem.IsWindows())
        {
            Core.Utils.TimeEndPeriod(1);
        }

        _fileLogger?.Flush();
        Environment.Exit(ModuleProcessExitCodes.OK);
        return ModuleProcessExitCodes.OK;
    }
}
