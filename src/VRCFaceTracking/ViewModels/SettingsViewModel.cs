using System.Diagnostics;
using System.Globalization;
using System.Text;
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

internal sealed partial class SettingsViewModel : ObservableRecipient
{
    public const string ThemeSettingKey = "AppTheme";

    private static readonly CompositeFormat LogFolderPendingFormat = CompositeFormat.Parse(Strings.Resources.LogFolderPendingDescription);

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
    public DeveloperSettings Developer
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

    public string AutoStartDescription => IsOpenVREnabled
        ? Strings.Resources.AutoStartSettings_Description
        : Strings.Resources.AutoStartSettingsUnavailableDescription;

    public void RefreshOpenVrState()
    {
        _openVRService.InitIfNotAlready();
        OnPropertyChanged(nameof(IsOpenVREnabled));
        OnPropertyChanged(nameof(AutoStartDescription));
        OnPropertyChanged(nameof(AutoStart));
    }

    public static bool IsOpenVRSupported => OpenVRService.IsSupported;

    public bool ExitWithSteamVr
    {
        get => _openVRService.ExitWithSteamVr;
        set
        {
            _openVRService.ExitWithSteamVr = value;
            _ = _localSettingsService.SaveAsync(_openVRService, CancellationToken.None);
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
            _ = _localSettingsService.SaveAsync(_loggingSettings, CancellationToken.None);
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
            int clamped = (int)Math.Clamp(value, 1, 200);
            if (clamped == _loggingSettings.LogFilesToKeep)
            {
                return;
            }

            _loggingSettings.LogFilesToKeep = clamped;
            _ = _localSettingsService.SaveAsync(_loggingSettings, CancellationToken.None);
            OnPropertyChanged();
        }
    }

    public static string LogDirectory => LogDirectories.Current;

    [ObservableProperty] private string _pendingLogDirectory = LogDirectories.Current;

    public string LogFolderDescription => string.Equals(PendingLogDirectory, LogDirectories.Current, StringComparison.Ordinal)
        ? LogDirectories.Current
        : string.Format(CultureInfo.CurrentCulture, LogFolderPendingFormat, PendingLogDirectory);

    public bool HasCustomLogFolder => !string.Equals(PendingLogDirectory, LogDirectories.Default, StringComparison.Ordinal);

    public async Task SetLogDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        await _localSettingsService.SaveSettingAsync(LogDirectories.SettingKey, path, forceLocal: false, cancellationToken);
        PendingLogDirectory = path;
        OnPropertyChanged(nameof(LogFolderDescription));
        OnPropertyChanged(nameof(HasCustomLogFolder));
    }

    public ICommand ResetLogDirectoryCommand
    {
        get;
    }

    public static bool IsUpdateCheckAvailable => BuildInfo.Channel != BuildChannel.Dev;

    public bool CheckUpdatesOnStartup
    {
        get => _updateSettings.CheckOnStartup;
        set
        {
            _updateSettings.CheckOnStartup = value;
            _ = _localSettingsService.SaveAsync(_updateSettings, CancellationToken.None);
            OnPropertyChanged();
        }
    }

    public static string VersionText => $"{BuildInfo.VersionString} ({BuildInfo.ChannelName})";

    public SettingsViewModel(
        OpenVRService openVRService,
        DeveloperSettings developerSettings,
        IOscTarget oscTarget,
        RiskySettingsViewModel riskySettingsViewModel,
        LoggingSettings loggingSettings,
        LogLevelGate logGate,
        ILocalSettingsService localSettingsService,
        UpdateSettings updateSettings,
        UpdateService updateService)
    {
        _openVRService = openVRService;
        Developer = developerSettings;
        OscTarget = oscTarget;
        RiskySettings = riskySettingsViewModel;
        _loggingSettings = loggingSettings;
        _logGate = logGate;
        _localSettingsService = localSettingsService;
        _updateSettings = updateSettings;
        _updateService = updateService;

        OpenLogsCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(LogDirectories.Current);
            Process.Start(new ProcessStartInfo(LogDirectories.Current) { UseShellExecute = true });
        });

        CheckUpdatesCommand = new AsyncRelayCommand(cancellationToken => _updateService.CheckAsync(manual: true, cancellationToken));
        ResetLogDirectoryCommand = new AsyncRelayCommand(cancellationToken => SetLogDirectoryAsync(LogDirectories.Default, cancellationToken));

        _ = LoadContributorsAsync(CancellationToken.None);
    }

    public Task<string?> ReadThemeAsync(CancellationToken cancellationToken) =>
        _localSettingsService.ReadSettingAsync<string?>(ThemeSettingKey, defaultValue: null, forceLocal: false, cancellationToken);

    public Task SaveThemeAsync(string theme, CancellationToken cancellationToken) =>
        _localSettingsService.SaveSettingAsync(ThemeSettingKey, theme, forceLocal: false, cancellationToken);

    private async Task LoadContributorsAsync(CancellationToken cancellationToken)
    {
        List<GithubContributor> bundled = GithubService.GetBundledContributors();
        if (bundled.Count > 0)
        {
            Contributors = bundled;
            return;
        }
        Contributors = await GithubService.GetContributorsAsync("benaclejames/VRCFaceTracking", cancellationToken);
    }
}
