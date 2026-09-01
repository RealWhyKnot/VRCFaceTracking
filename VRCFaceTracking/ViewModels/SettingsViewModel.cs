using System.Diagnostics;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.UI.Xaml;

using VRCFaceTracking.Contracts.Services;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Models;
using VRCFaceTracking.Services;

namespace VRCFaceTracking.ViewModels;

public partial class SettingsViewModel : ObservableRecipient
{
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly LoggingSettings _loggingSettings;
    private readonly LogLevelGate _logGate;
    private readonly ILocalSettingsService _localSettingsService;
    [ObservableProperty] private ElementTheme _elementTheme;
    [ObservableProperty] private List<GithubContributor> _contributors;

    public ICommand SwitchThemeCommand
    {
        get;
    }

    public ICommand OpenLogsCommand
    {
        get;
    }

    private GithubService GithubService
    {
        get;
        set;
    }

    private OpenVRService OpenVRService
    {
        get;
    }

    public bool AutoStart
    {
        get => OpenVRService.AutoStart;
        set
        {
            OpenVRService.AutoStart = value;
            OnPropertyChanged();
        }
    }

    public bool IsOpenVREnabled => OpenVRService.IsInitialized;

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

    public bool IsVerboseToggleEnabled => !BuildInfo.VerboseForced;

    public string VerboseDescription => BuildInfo.VerboseForced
        ? "VerboseLoggingForcedDescription".GetLocalized()
        : "VerboseLoggingDescription".GetLocalized();

    public double LogFilesToKeep
    {
        get => _loggingSettings.LogFilesToKeep;
        set
        {
            var clamped = double.IsNaN(value) ? LoggingSettings.DefaultLogFilesToKeep : (int)Math.Clamp(value, 1, 200);
            if (clamped == _loggingSettings.LogFilesToKeep)
            {
                return;
            }

            _loggingSettings.LogFilesToKeep = clamped;
            _ = _localSettingsService.Save(_loggingSettings);
            OnPropertyChanged();
        }
    }

    public string LogDirectory => Core.Utils.LogDirectory;

    public string VersionText => $"{BuildInfo.VersionString} ({BuildInfo.ChannelName})";

    private async void LoadContributors()
    {
        Contributors = await GithubService.GetContributors("benaclejames/VRCFaceTracking");
    }

    public SettingsViewModel(IThemeSelectorService themeSelectorService, GithubService githubService, OpenVRService openVRService,
        LoggingSettings loggingSettings, LogLevelGate logGate, ILocalSettingsService localSettingsService)
    {
        _themeSelectorService = themeSelectorService;
        _loggingSettings = loggingSettings;
        _logGate = logGate;
        _localSettingsService = localSettingsService;
        GithubService = githubService;
        OpenVRService = openVRService;

        _elementTheme = _themeSelectorService.Theme;

        SwitchThemeCommand = new RelayCommand<ElementTheme>(
            async (param) =>
            {
                if (ElementTheme != param)
                {
                    ElementTheme = param;
                    await _themeSelectorService.SetThemeAsync(param);
                }
            });

        OpenLogsCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(Core.Utils.LogDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", Core.Utils.LogDirectory) { UseShellExecute = true });
        });

        OpenVRService.InitIfNotAlready();
        LoadContributors();
    }
}
