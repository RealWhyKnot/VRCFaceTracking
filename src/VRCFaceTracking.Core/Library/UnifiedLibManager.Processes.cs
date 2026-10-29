using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;

namespace VRCFaceTracking.Core.Library;

public sealed partial class UnifiedLibManager
{
    private const int HeartbeatIntervalMs = 50;
    private const double PokeGapWarnMs = HeartbeatIntervalMs + 50;

    private void StartModuleProcesses(string[] dlls)
    {
        foreach (string dll in dlls)
        {
            InitProgress.Add(dll, Path.GetFileNameWithoutExtension(dll));
        }

        for (int i = 0; i < dlls.Length; i++)
        {
            if (!StartModuleProcess(dlls[i], new ModuleMetadataInternal(), i))
            {
                InitProgress.Resolve(dlls[i]);
            }
        }
    }

    private bool StartModuleProcess(string dll, ModuleMetadataInternal metadata, int spawnOrder)
    {
        try
        {
            metadata.ModulePath = dll;
            metadata.AllowedCapabilities = StoredCapabilityOverride(dll);
            VrcftSandboxServer server = SandboxServer ?? throw new InvalidOperationException("The sandbox server is not running");
            string verboseFlag = _logGate.Verbose ? " --verbose" : string.Empty;
            Process sandboxProcess = Process.Start(new ProcessStartInfo(
                SandboxProcessPath, $"--port {server.Port} --module-path \"{dll}\" --parent-pid {Environment.ProcessId}{verboseFlag}"
            )
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            })!;

            int pid = sandboxProcess.Id;

            var runtimeInfo = new ModuleRuntimeInfo()
            {
                SandboxProcessPID = pid,
                SandboxProcessPort = -1,
                SandboxModulePath = dll,
                IsActive = true,
                Process = sandboxProcess,
                ModuleClassName = Path.GetFileNameWithoutExtension(dll),
                ModuleInformation = metadata,
                SpawnOrder = spawnOrder,
            };
            lock (_modulesLock)
            {
                LogStartedModuleProcess(_logger, pid, dll);
                _availableSandboxModules.Add(runtimeInfo);
            }
            runtimeInfo.Watcher = new ModuleProcessWatcher(sandboxProcess, runtimeInfo.ModuleClassName, _moduleLogger, code => OnModuleProcessExited(runtimeInfo, code));
            return true;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            LogFailedStartSandboxProcessSkipping(_logger, e.Message, dll);
            return false;
        }
    }

    private void OnModuleProcessExited(ModuleRuntimeInfo module, int exitCode)
    {
        module.UpdateCancellationToken?.Cancel();
        module.IsActive = false;

        string description = ModuleProcessExitCodes.Describe(exitCode);
        if (exitCode == ModuleProcessExitCodes.OK || module.TeardownRequested || IsTearingDown)
        {
            LogModuleProcessExited(_logger, module.ModuleClassName, description);
            return;
        }

        string stderrTail = module.Watcher?.StderrTail ?? string.Empty;
        LogModuleProcessStoppedUnexpectedlyLast(_logger, module.ModuleClassName, description, Environment.NewLine, stderrTail);

        if (ModuleRestartPolicy.IsRestartableExitCode(exitCode) && !string.IsNullOrEmpty(module.SandboxModulePath))
        {
            TimeSpan? delay = _restartPolicy.NextRestartDelay(module.SandboxModulePath, _timeProvider.GetUtcNow().UtcDateTime);
            if (delay.HasValue)
            {
                int attempt = _restartPolicy.AttemptNumber(module.SandboxModulePath);
                LogRestartingAfterAttempt(_logger, module.ModuleClassName, description, attempt, ModuleRestartPolicy.MaxAttempts);
                _ = RestartModuleAsync(module, delay.Value, Volatile.Read(ref _initGeneration), CancellationToken.None);
                return;
            }

            LogCrashedTimesWithinLeavingStopped(_logger, module.ModuleClassName, ModuleRestartPolicy.MaxAttempts, ModuleRestartPolicy.Window.TotalSeconds);
        }

        MarkModuleCrashed(module, description);
    }

    private void MarkModuleCrashed(ModuleRuntimeInfo module, string description)
    {
        InitProgress.Resolve(module.SandboxModulePath);
        _dispatcherService.Run(() =>
        {
            ModuleMetadataInternal info = module.ModuleInformation;
            if (string.IsNullOrEmpty(info.Name))
            {
                info.Name = module.ModuleClassName;
            }

            if (LoadedModulesMetadata.Count > 0 && LoadedModulesMetadata[0].IsPlaceholder)
            {
                LoadedModulesMetadata.RemoveAt(0);
            }

            if (!LoadedModulesMetadata.Contains(info))
            {
                LoadedModulesMetadata.Add(info);
            }

            info.Active = false;
            module.Status = ModuleState.Crashed;
            info.CrashDescription = description;
            info.Crashed = true;
            RecomputeCapabilityAssignments();
        });
    }

    private async Task RestartModuleAsync(ModuleRuntimeInfo deadModule, TimeSpan delay, int generation, CancellationToken cancellationToken)
    {
        lock (_modulesLock)
        {
            _moduleThreads.Remove(deadModule);
            _availableSandboxModules.Remove(deadModule);
        }

        ModuleMetadataInternal info = deadModule.ModuleInformation;
        ApplyAssignment(info, TrackingCapability.None);
        RecomputeCapabilityAssignments();

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        if (IsTearingDown || deadModule.TeardownRequested || generation != Volatile.Read(ref _initGeneration))
        {
            return;
        }

        info.Crashed = false;
        info.CrashDescription = string.Empty;

        deadModule.Process?.Dispose();

        if (deadModule.StatusChangedHandler != null)
        {
            info.PropertyChanged -= deadModule.StatusChangedHandler;
            deadModule.StatusChangedHandler = null;
        }

        if (StartModuleProcess(deadModule.SandboxModulePath, info, deadModule.SpawnOrder))
        {
            LogRestartedModuleProcess(_logger, deadModule.ModuleClassName);
        }
        else
        {
            MarkModuleCrashed(deadModule, "failed to restart the module process");
        }
    }

    private static void AttemptSandboxedModuleInitialize(ModuleRuntimeInfo module)
    {
        var eventGetSupportedPacket = new EventInitGetSupported();

        if (module.SandboxProcessPID != -1 && module.SandboxProcessPort > 0)
        {
            SandboxServer?.SendData(eventGetSupportedPacket, module.SandboxProcessPort);
        }
        else
        {
            module.EventBus.Enqueue(new QueuedPacket(eventGetSupportedPacket, module.SandboxProcessPort));
        }
    }

    private void EnsureModuleThreadStartedSandboxed(ModuleRuntimeInfo module)
    {
        lock (_modulesLock)
        {
            if (_moduleThreads.Any(pair =>
                (pair.SandboxProcessPID == module.SandboxProcessPID) &&
                (pair.SandboxProcessPort == module.SandboxProcessPort)
            ))
            {
                return;
            }
        }

        int port = module.SandboxProcessPort;
        string moduleName = module.ModuleClassName;
        var cts = new CancellationTokenSource();
        CancellationToken token = cts.Token;
        var thread = new Thread(() => RunHeartbeat(moduleName, port, token))
        {
            IsBackground = true,
        };
        module.UpdateCancellationToken = cts;
        module.UpdateThread = thread;
        thread.Start();

        lock (_modulesLock)
        {
            _moduleThreads.Add(module);
        }
    }

    private void RunHeartbeat(string moduleName, int port, CancellationToken cancellationToken)
    {
        LogStartingThread(_logger, moduleName);
        var updatePacket = new EventUpdatePacket();
        double toMs = 1000.0 / Stopwatch.Frequency;
        long lastPoke = Stopwatch.GetTimestamp();
        bool sendFailing = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Thread.Sleep(HeartbeatIntervalMs);
                long now = Stopwatch.GetTimestamp();
                double gapMs = (now - lastPoke) * toMs;
                lastPoke = now;
                if (gapMs > PokeGapWarnMs && _logger.IsEnabled(LogLevel.Debug))
                {
                    LogDiagPokeGapMsPort(_logger, gapMs, port);
                }
                updatePacket.SentTicks = now;
                SandboxServer?.SendData(updatePacket, port);
                sendFailing = false;
            }
            catch (ThreadInterruptedException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException e)
            {
                if (!sendFailing)
                {
                    LogHeartbeatSendsFailing(_logger, port, e.SocketErrorCode);
                }
                sendFailing = true;
            }
        }
        LogThreadEnded(_logger, moduleName);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started module process {Pid} for {DllPath}")]
    private static partial void LogStartedModuleProcess(ILogger logger, int pid, string dllPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Error} Failed to start sandbox process for {Path}. Skipping...")]
    private static partial void LogFailedStartSandboxProcessSkipping(ILogger logger, string error, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Module process for {Module} exited ({Description})")]
    private static partial void LogModuleProcessExited(ILogger logger, string module, string description);

    [LoggerMessage(Level = LogLevel.Error, Message = "Module process for {Module} stopped unexpectedly: {Description}. Last stderr lines:{Newline}{Tail}")]
    private static partial void LogModuleProcessStoppedUnexpectedlyLast(ILogger logger, string module, string description, string newline, string tail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restarting {Module} after {Description} (attempt {Attempt}/{Max})")]
    private static partial void LogRestartingAfterAttempt(ILogger logger, string module, string description, int attempt, int max);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Module} crashed {Max} times within {Window}s; leaving it stopped")]
    private static partial void LogCrashedTimesWithinLeavingStopped(ILogger logger, string module, int max, double window);

    [LoggerMessage(Level = LogLevel.Information, Message = "Restarted module process for {Module}")]
    private static partial void LogRestartedModuleProcess(ILogger logger, string module);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting thread for {Module}")]
    private static partial void LogStartingThread(ILogger logger, string module);

    [LoggerMessage(Level = LogLevel.Debug, Message = "diag.poke gap {Gap:N1}ms on port {Port}")]
    private static partial void LogDiagPokeGapMsPort(ILogger logger, double gap, int port);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Heartbeats to the module on port {Port} are failing ({Error})")]
    private static partial void LogHeartbeatSendsFailing(ILogger logger, int port, SocketError error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Thread for {Module} ended")]
    private static partial void LogThreadEnded(ILogger logger, string module);
}
