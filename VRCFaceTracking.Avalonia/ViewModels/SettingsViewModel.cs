using System.Diagnostics;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.Updates;
using VRCFaceTracking.Models;
using VRCFaceTracking.Services;

namespace VRCFaceTracking.ViewModels;

public partial class SettingsViewModel : ObservableRecipient
{
    public const string ThemeSettingKey = "AppTheme";

    private readonly LoggingSettings _loggingSettings;
    private readonly LogLevelGate _logGate;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly UpdateSettings _updateSettings;
    private readonly UpdateService _updateService;
    private readonly OpenVRService _openVRService;

    [ObservableProperty] private List<GithubContributor> _contributors = [];

    public IOscTarget OscTarget
    {
        get;
    }
    public RiskySettingsViewModel RiskySettings
    {
        get;
    }

    public ICommand OpenLogsCommand
    {
        get;
    }
    public ICommand CheckUpdatesCommand
    {
        get;
    }

    public bool AutoStart
    {
        get => _openVRService.AutoStart;
        set
        {
            _openVRService.AutoStart = value;
            OnPropertyChanged();
        }
    }

    public bool IsOpenVREnabled => _openVRService.IsInitialized;

    public static bool IsOpenVRSupported => OpenVRService.IsSupported;

    public bool ExitWithSteamVr
    {
        get => _openVRService.ExitWithSteamVr;
        set
        {
            _openVRService.ExitWithSteamVr = value;
            _ = _localSettingsService.Save(_openVRService);
            OnPropertyChanged();
        }
    }

    public bool VerboseLogging
    {
        get => _loggingSettings.VerboseEffective;
        set
        {
            _loggingSettings.VerboseLogging = value;
            _logGate.Set(_loggingSettings.VerboseEffective);
            _ = _localSettingsService.Save(_loggingSettings);
            OnPropertyChanged();
        }
    }

    public static bool IsVerboseToggleEnabled => !BuildInfo.VerboseForced;

    public static string VerboseDescription => BuildInfo.VerboseForced
        ? Strings.Resources.VerboseLoggingForcedDescription
        : Strings.Resources.VerboseLoggingDescription;

    public decimal LogFilesToKeep
    {
        get => _loggingSettings.LogFilesToKeep;
        set
        {
            var clamped = (int)Math.Clamp(value, 1, 200);
            if (clamped == _loggingSettings.LogFilesToKeep)
            {
                return;
            }

            _loggingSettings.LogFilesToKeep = clamped;
            _ = _localSettingsService.Save(_loggingSettings);
            OnPropertyChanged();
        }
    }

    public static string LogDirectory => Core.Utils.LogDirectory;

    public static bool IsUpdateCheckAvailable => BuildInfo.Channel != BuildChannel.Dev;

    public bool CheckUpdatesOnStartup
    {
        get => _updateSettings.CheckOnStartup;
        set
        {
            _updateSettings.CheckOnStartup = value;
            _ = _localSettingsService.Save(_updateSettings);
            OnPropertyChanged();
        }
    }

    public static string VersionText => $"{BuildInfo.VersionString} ({BuildInfo.ChannelName})";

    public SettingsViewModel(
        GithubService githubService,
        OpenVRService openVRService,
        IOscTarget oscTarget,
        RiskySettingsViewModel riskySettingsViewModel,
        LoggingSettings loggingSettings,
        LogLevelGate logGate,
        ILocalSettingsService localSettingsService,
        UpdateSettings updateSettings,
        UpdateService updateService)
    {
        _openVRService = openVRService;
        OscTarget = oscTarget;
        RiskySettings = riskySettingsViewModel;
        _loggingSettings = loggingSettings;
        _logGate = logGate;
        _localSettingsService = localSettingsService;
        _updateSettings = updateSettings;
        _updateService = updateService;

        OpenLogsCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(Core.Utils.LogDirectory);
            Process.Start(new ProcessStartInfo(Core.Utils.LogDirectory) { UseShellExecute = true });
        });

        CheckUpdatesCommand = new AsyncRelayCommand(() => _updateService.CheckAsync(true));

        _openVRService.InitIfNotAlready();
        LoadContributors(githubService);
    }

    public Task<string?> ReadThemeAsync() => _localSettingsService.ReadSettingAsync<string?>(ThemeSettingKey);

    public Task SaveThemeAsync(string theme) => _localSettingsService.SaveSettingAsync(ThemeSettingKey, theme);

    private async void LoadContributors(GithubService githubService)
    {
        try
        {
            var bundled = githubService.GetBundledContributors();
            if (bundled.Count > 0)
            {
                Contributors = bundled;
                return;
            }
            Contributors = await githubService.GetContributors("benaclejames/VRCFaceTracking");
        }
        catch
        {
        }
    }
}
