using System.ComponentModel;
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
using VRCFaceTracking.Core.Services;
using VRCFaceTracking.Core.Updates;
using VRCFaceTracking.Core.Validation;
using VRCFaceTracking.Helpers;
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
    private readonly ExternalOscInputService _externalOsc;

    [ObservableProperty] private List<GithubContributor> _contributors = [];

    public IOscTarget OscTarget
    {
        get;
    }

    public OscFieldViewModel InPortField
    {
        get;
    }

    public OscFieldViewModel OutPortField
    {
        get;
    }

    public OscFieldViewModel AddressField
    {
        get;
    }

    public OscFieldViewModel ExternalOscPortField
    {
        get;
    }

    public bool ExternalOscEnabled
    {
        get => _externalOsc.Settings.Enabled;
        set
        {
            if (value == _externalOsc.Settings.Enabled)
            {
                return;
            }

            _externalOsc.Settings.Enabled = value;
            _externalOsc.Apply();
            _ = _externalOsc.SaveAsync(CancellationToken.None);
            OnPropertyChanged();
            RefreshExternalOsc();
        }
    }

    public string ExternalOscStatusText { get; private set; } = string.Empty;

    public bool HasExternalOscStatus => ExternalOscStatusText.Length > 0;

    public static bool IsStartupVisible => IsOpenVRSupported || IsUpdateCheckAvailable;
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

    public bool ShapeLogging
    {
        get => _loggingSettings.ShapeLoggingEffective;
        set
        {
            _loggingSettings.ShapeLoggingChoice = value ? LoggingSettings.ShapeLoggingOn : LoggingSettings.ShapeLoggingOff;
            _ = _localSettingsService.SaveAsync(_loggingSettings, CancellationToken.None);
            OnPropertyChanged();
        }
    }

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
        UpdateService updateService,
        ExternalOscInputService externalOsc)
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
        _externalOsc = externalOsc;

        InPortField = new OscFieldViewModel(() => OscTarget.InPort.ToString(CultureInfo.CurrentCulture), text => ApplyPort(text, value => OscTarget.InPort = value));
        OutPortField = new OscFieldViewModel(() => OscTarget.OutPort.ToString(CultureInfo.CurrentCulture), text => ApplyPort(text, value => OscTarget.OutPort = value));
        AddressField = new OscFieldViewModel(() => OscTarget.DestinationAddress ?? string.Empty, ApplyAddress);
        ExternalOscPortField = new OscFieldViewModel(() => _externalOsc.Settings.Port.ToString(CultureInfo.CurrentCulture), ApplyExternalPort);
        OscTarget.PropertyChanged += OnOscTargetChanged;
        RefreshExternalOsc();

        OpenLogsCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(LogDirectories.Current);
            Process.Start(new ProcessStartInfo(LogDirectories.Current) { UseShellExecute = true });
        });

        CheckUpdatesCommand = new AsyncRelayCommand(cancellationToken => _updateService.CheckAsync(manual: true, cancellationToken));
        ResetLogDirectoryCommand = new AsyncRelayCommand(cancellationToken => SetLogDirectoryAsync(LogDirectories.Default, cancellationToken));

        _ = LoadContributorsAsync(CancellationToken.None);
    }

    public void ReloadOscFields()
    {
        InPortField.Reload();
        OutPortField.Reload();
        AddressField.Reload();
        ExternalOscPortField.Reload();
        OnPropertyChanged(nameof(ExternalOscEnabled));
        RefreshExternalOsc();
    }

    public void RefreshExternalOsc()
    {
        ExternalOscStatus status = _externalOsc.Status;
        string text = status.State switch
        {
            ExternalOscState.Listening => TrackingText.Format(Strings.Resources.ExternalOsc_Listening, _externalOsc.Settings.Port),
            ExternalOscState.Receiving => TrackingText.Format(Strings.Resources.ExternalOsc_Receiving, status.PacketsPerSecond),
            ExternalOscState.Error => Strings.Resources.ExternalOsc_Unavailable,
            ExternalOscState.Disabled => string.Empty,
            _ => string.Empty,
        };
        if (string.Equals(text, ExternalOscStatusText, StringComparison.Ordinal))
        {
            return;
        }

        bool had = HasExternalOscStatus;
        ExternalOscStatusText = text;
        OnPropertyChanged(nameof(ExternalOscStatusText));
        if (had != HasExternalOscStatus)
        {
            OnPropertyChanged(nameof(HasExternalOscStatus));
        }
    }

    private string? ApplyExternalPort(string text)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port))
        {
            return Strings.Resources.Settings_Port_Error;
        }

        ExternalOscPortCheck check = ExternalOscInputSettings.Check(port, OscTarget);
        if (check != ExternalOscPortCheck.Valid)
        {
            return check == ExternalOscPortCheck.OutOfRange ? Strings.Resources.Settings_Port_Error : Strings.Resources.ExternalOsc_PortConflict;
        }

        _externalOsc.Settings.Port = port;
        _externalOsc.Apply();
        _ = _externalOsc.SaveAsync(CancellationToken.None);
        RefreshExternalOsc();
        return null;
    }

    private static string? ApplyPort(string text, Action<int> commit)
    {
        if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is >= Core.Models.OscTarget.MinPort and <= Core.Models.OscTarget.MaxPort)
        {
            commit(port);
            return null;
        }

        return Strings.Resources.Settings_Port_Error;
    }

    private string? ApplyAddress(string text)
    {
        string address = text.Trim();
        if (!new ValidIpAddressAttribute().IsValid(address))
        {
            return Strings.Resources.Settings_Address_Error;
        }

        OscTarget.DestinationAddress = address;
        return null;
    }

    private void OnOscTargetChanged(object? sender, PropertyChangedEventArgs e)
    {
        InPortField.Sync();
        OutPortField.Sync();
        AddressField.Sync();
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
