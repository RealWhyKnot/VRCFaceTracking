using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;

[assembly: TypeForwardedTo(typeof(VRCFaceTracking.ExtTrackingModule))]
[assembly: TypeForwardedTo(typeof(VRCFaceTracking.ModuleMetadata))]
[assembly: TypeForwardedTo(typeof(ModuleState))]

namespace VRCFaceTracking.Core;

public class MainStandalone : IMainService
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

    public async Task Teardown()
    {
        _logger.LogInformation("VRCFT Standalone Exiting!");
        await _mutator.Save();

        _libManager.TeardownAllAndReset();

        if (OperatingSystem.IsWindows())
        {
            _logger.LogDebug("Resetting our time end period...");
            var timeEndRes = Utils.TimeEndPeriod(1);
            if (timeEndRes != 0)
            {
                _logger.LogWarning($"TimeEndPeriod failed with HRESULT {timeEndRes}");
            }
        }

        _logger.LogDebug("Teardown complete. Awaiting exit...");
    }

    public async Task InitializeAsync()
    {
        try
        {
            VRChat.EnsureVRCOSCDirectory();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not locate the VRChat OSC directory: {message}", ex.Message);
        }

        if (string.IsNullOrEmpty(VRChat.VRCOSCDirectory))
        {
            _logger.LogInformation("No local VRChat install found; avatar configs will come from OSCQuery only.");
        }

        // Ensure OSC is enabled
        var isWindows = OperatingSystem.IsWindows();

        if (isWindows && VRChat.ForceEnableOsc()) // If osc was previously not enabled
        {
            _logger.LogWarning("VRCFT detected OSC was disabled and automatically enabled it.");
            // If we were launched after VRChat
            if (VRChat.IsVrChatRunning())
                _logger.LogError(
                    "However, VRChat was running while this change was made.\n" +
                    "If parameters do not update, please restart VRChat or manually enable OSC yourself in your avatar's expressions menu.");
        }

        await _mutator.Load();

        // Begin main OSC update loop
        _logger.LogDebug("Starting OSC update loop...");

        if (isWindows)
        {
            if (Utils.AlwaysHonorTimerResolution(out var error))
            {
                _logger.LogDebug("Timer resolution requests will be honored while the window is occluded");
            }
            else
            {
                _logger.LogWarning("Could not opt out of timer resolution throttling (error {Error}); update rate drops to 64 Hz while the window is covered", error);
            }
            Utils.TimeBeginPeriod(1);
            var priority = Process.GetCurrentProcess().PriorityClass;
            if (priority == ProcessPriorityClass.AboveNormal)
            {
                _logger.LogDebug("Process priority class is {Priority}", priority);
            }
            else
            {
                _logger.LogWarning("Process priority class is {Priority}; tracking may stall while the window is in the background", priority);
            }
        }
    }
}
