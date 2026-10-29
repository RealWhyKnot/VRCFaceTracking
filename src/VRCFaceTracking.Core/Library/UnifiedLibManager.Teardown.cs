using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;

namespace VRCFaceTracking.Core.Library;

public sealed partial class UnifiedLibManager
{
    public void TeardownAllAndReset()
    {
        LogTearingDownAllModules(_logger);
        TeardownInProgress = true;
        try
        {
            ModuleRuntimeInfo[] threadModules;
            ModuleRuntimeInfo[] sandboxModules;
            lock (_modulesLock)
            {
                threadModules = [.. _moduleThreads];
                sandboxModules = [.. _availableSandboxModules];
            }

            foreach (ModuleRuntimeInfo? module in threadModules.Concat(sandboxModules))
            {
                module?.TeardownRequested = true;
            }

            foreach (ModuleRuntimeInfo module in threadModules)
            {
                TeardownOneModule(module);
            }
            lock (_modulesLock)
            {
                _moduleThreads.Clear();
            }

            foreach (ModuleRuntimeInfo module in sandboxModules)
            {
                TeardownOneModule(module);
            }
            lock (_modulesLock)
            {
                _availableSandboxModules.Clear();
            }

            foreach (ModuleRuntimeInfo? module in threadModules.Concat(sandboxModules).Distinct())
            {
                module?.Process?.Dispose();
            }

            EyeStatus = ModuleState.Uninitialized;
            ExpressionStatus = ModuleState.Uninitialized;
        }
        finally
        {
            TeardownInProgress = false;
        }
    }

    public static void ShutdownSandboxServer()
    {
        VrcftSandboxServer? server = SandboxServer;
        SandboxServer = null;
        server?.Close();
    }

    private void TeardownOneModule(ModuleRuntimeInfo module)
    {
        if (module == null || (module.Process?.HasExited ?? true))
        {
            return;
        }

        bool success = false;
        try
        {
            success = TeardownModuleSandboxed(module);
        }
        finally
        {
            if (!success)
            {
                LogModuleFailedShutDownKilling(_logger, DisplayName(module));
                module.UpdateThread?.Interrupt();
            }
        }
    }

    private bool TeardownModuleSandboxed(ModuleRuntimeInfo module)
    {
        LogTearingDown(_logger, module.ModuleClassName);
        module.TeardownRequested = true;

        if (module.SandboxProcessPort > 0)
        {
            SandboxServer?.SendData(new EventTeardownPacket(), module.SandboxProcessPort);
        }

        module.UpdateCancellationToken?.Cancel();
        Thread.Sleep(100);

        if (!module.Process.HasExited && !WaitForExitOrKill(module.Process))
        {
            return false;
        }

        if (module.UpdateThread?.IsAlive ?? false)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                string moduleName = DisplayName(module);
                LogWaitingThreadJoin(_logger, moduleName);
            }
            module.UpdateThread?.Join(500);
        }

        return true;
    }

    private bool WaitForExitOrKill(Process process)
    {
        LogModuleProcessNotYetExited(_logger);
        try
        {
            if (process.WaitForExit(1000))
            {
                return true;
            }

            LogModuleDidnExitGracefullyForcing(_logger, process.Id);
            process.Kill(entireProcessTree: true);
            if (process.WaitForExit(2000))
            {
                return true;
            }

            if (OperatingSystem.IsWindows())
            {
                using var killer = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/F /T /PID {process.Id}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
                killer?.WaitForExit(2000);
            }
            else
            {
                LogProcessZombieStuckKernelI(_logger, process.Id);
            }
            return false;
        }
        catch (Win32Exception ex)
        {
            LogKillWin32Error(_logger, ex, process.Id, ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or AggregateException)
        {
            LogTriedKillingProcessPidGot(_logger, process.Id, ex.HResult, ex.Message);
        }

        return true;
    }

    private static string DisplayName(ModuleRuntimeInfo module) =>
        module.ModuleInformation.Name is { Length: > 0 } name ? name : module.ModuleClassName;

    [LoggerMessage(Level = LogLevel.Information, Message = "Tearing down all modules...")]
    private static partial void LogTearingDownAllModules(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Module: {Module} failed to shut down. Killing its thread.")]
    private static partial void LogModuleFailedShutDownKilling(ILogger logger, string module);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tearing down {Module} ")]
    private static partial void LogTearingDown(ILogger logger, string module);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Module process has not yet exited")]
    private static partial void LogModuleProcessNotYetExited(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Module {Id} didn't exit gracefully. Forcing kill...")]
    private static partial void LogModuleDidnExitGracefullyForcing(ILogger logger, int id);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Process {Id} is a zombie or stuck in Kernel I/O. Manual intervention required.")]
    private static partial void LogProcessZombieStuckKernelI(ILogger logger, int id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tried killing process with PID {ProcessId}. Got exception ({HResult}) {ErrorMessage}")]
    private static partial void LogTriedKillingProcessPidGot(ILogger logger, int processId, int hResult, string errorMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tried killing process with PID {ProcessId}. Got win32 error: {ErrorMessage}")]
    private static partial void LogKillWin32Error(ILogger logger, Exception exception, int processId, string errorMessage);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Waiting for {Module}'s thread to join...")]
    private static partial void LogWaitingThreadJoin(ILogger logger, string module);
}
