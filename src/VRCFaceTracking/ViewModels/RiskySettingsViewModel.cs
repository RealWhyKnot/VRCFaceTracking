using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.ViewModels;

internal sealed partial class RiskySettingsViewModel : ObservableObject
{
    private readonly IMainService _mainService;
    private readonly ILogger<RiskySettingsViewModel> _logger;

    [ObservableProperty] private bool _enabled;

    public RiskySettingsViewModel(IMainService mainService, ILogger<RiskySettingsViewModel> logger)
    {
        _mainService = mainService;
        _logger = logger;
    }

    public bool AllRelevantDebug
    {
        get => ParameterSenderService.AllParametersRelevant;
        set
        {
            ParameterSenderService.AllParametersRelevant = value;
            OnPropertyChanged();
        }
    }

    public async Task ForceReInitAsync(CancellationToken cancellationToken)
    {
        LogReinitializingVrcft(_logger);

        await _mainService.TeardownAsync(cancellationToken);

        await _mainService.InitializeAsync(cancellationToken);
    }

    public void ResetVRCFT()
    {
        LogResettingVrcft(_logger);

        File.WriteAllBytes(Path.Combine(Core.Utils.PersistentDataDirectory, "reset"), []);
    }

    public void ResetAvatarOscManifests()
    {
        LogResettingVRChatAvatarConfiguration(_logger);
        try
        {
            if (string.IsNullOrEmpty(VRChat.VRCOSCDirectory) || !Directory.Exists(VRChat.VRCOSCDirectory))
            {
                return;
            }

            foreach (string userFolder in Directory.GetDirectories(VRChat.VRCOSCDirectory))
            {
                if (Directory.Exists(userFolder + "\\Avatars"))
                {
                    Directory.Delete(userFolder + "\\Avatars", recursive: true);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogFailedResetVRChatAvatarConfiguration(_logger, e, e.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reinitializing VRCFT...")]
    private static partial void LogReinitializingVrcft(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resetting VRCFT...")]
    private static partial void LogResettingVrcft(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resetting VRChat avatar configuration...")]
    private static partial void LogResettingVRChatAvatarConfiguration(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to reset VRChat avatar configuration! {Message}")]
    private static partial void LogFailedResetVRChatAvatarConfiguration(ILogger logger, Exception exception, string message);
}
