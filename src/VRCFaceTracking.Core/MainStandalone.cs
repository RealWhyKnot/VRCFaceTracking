using System.Diagnostics;
using System.Runtime;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Params.Data;

namespace VRCFaceTracking.Core;

public sealed partial class MainStandalone : IMainService
{
    private readonly ILogger<MainStandalone> _logger;
    private readonly ILibManager _libManager;
    private readonly UnifiedTrackingMutator _mutator;

    public Action<string, float> ParameterUpdate { get; set; } = (_, _) => { };

    public MainStandalone(
        ILogger<MainStandalone> logger,
        ILibManager libManager,
        UnifiedTrackingMutator mutator
        )
    {
        _logger = logger;
        _libManager = libManager;
        _mutator = mutator;
    }

    public async Task TeardownAsync(CancellationToken cancellationToken)
    {
        LogVrcftStandaloneExiting(_logger);
        await _mutator.SaveAsync(cancellationToken).ConfigureAwait(false);

        _libManager.TeardownAllAndReset();

        LogTeardownCompleteAwaitingExit(_logger);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            VRChat.EnsureVRCOSCDirectory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCouldNotLocateVRChatOsc(_logger, ex.Message);
        }

        if (string.IsNullOrEmpty(VRChat.VRCOSCDirectory))
        {
            LogNoLocalVRChatInstallFound(_logger);
        }

        bool isWindows = OperatingSystem.IsWindows();

        if (isWindows && VRChat.ForceEnableOsc())
        {
            LogVrcftDetectedOscDisabledAutomatically(_logger);
            if (VRChat.IsVrChatRunning())
            {
                LogHoweverVRChatRunningWhileChange(_logger);
            }
        }

        await _mutator.LoadAsync(cancellationToken).ConfigureAwait(false);

        LogStartingOscUpdateLoop(_logger);
        LogGCLatencyMode(_logger, GCSettings.LatencyMode);

        if (isWindows)
        {
            if (PowerThrottling.TryOptOut(out uint mask, out int error))
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    string state = PowerThrottling.Describe();
                    LogOptedOutPowerThrottlingMask(_logger, mask, state);
                }
            }
            else
            {
                LogCouldNotOptOutPower(_logger, error);
            }
            ProcessPriorityClass priority = Process.GetCurrentProcess().PriorityClass;
            if (priority == ProcessPriorityClass.AboveNormal)
            {
                LogProcessPriorityClass(_logger, priority);
            }
            else
            {
                LogProcessPriorityClassTrackingMay(_logger, priority);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "VRCFT Standalone Exiting!")]
    private static partial void LogVrcftStandaloneExiting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Teardown complete. Awaiting exit...")]
    private static partial void LogTeardownCompleteAwaitingExit(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not locate the VRChat OSC directory: {Message}")]
    private static partial void LogCouldNotLocateVRChatOsc(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "No local VRChat install found; avatar configs will come from OSCQuery only.")]
    private static partial void LogNoLocalVRChatInstallFound(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "VRCFT detected OSC was disabled and automatically enabled it.")]
    private static partial void LogVrcftDetectedOscDisabledAutomatically(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "However, VRChat was running while this change was made.\nIf parameters do not update, please restart VRChat or manually enable OSC yourself in your avatar's expressions menu.")]
    private static partial void LogHoweverVRChatRunningWhileChange(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting OSC update loop...")]
    private static partial void LogStartingOscUpdateLoop(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "GC latency mode is {Mode}")]
    private static partial void LogGCLatencyMode(ILogger logger, GCLatencyMode mode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Opted out of power throttling mask 0x{Mask:X}, {State}")]
    private static partial void LogOptedOutPowerThrottlingMask(ILogger logger, uint mask, string state);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not opt out of power throttling (error {Error}); update rate drops to 64 Hz while the window is covered")]
    private static partial void LogCouldNotOptOutPower(ILogger logger, int error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Process priority class is {Priority}")]
    private static partial void LogProcessPriorityClass(ILogger logger, ProcessPriorityClass priority);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Process priority class is {Priority}; tracking may stall while the window is in the background")]
    private static partial void LogProcessPriorityClassTrackingMay(ILogger logger, ProcessPriorityClass priority);
}
