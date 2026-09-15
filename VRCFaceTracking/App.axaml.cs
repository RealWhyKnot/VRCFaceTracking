using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using FluentAvalonia.Styling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Contracts.Services;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.Updates;
using VRCFaceTracking.Services;
using VRCFaceTracking.Views;

namespace VRCFaceTracking;

public partial class App : Application
{
    private static App? _instance;
    private ILogger? _logger;
    private static IHost? _host;

    public static bool StartServices = true;
    private static bool _dataValidatorTrimmed;

    internal static readonly LogLevelGate LogGate = new();
    private static readonly LoggingSettings LoggingSettings = new();
    private static readonly FileLoggerProvider FileLog = new(
        Core.Utils.LogDirectory,
        LogFileNames.Main(DateTime.Now),
        BuildInfo.HeaderBlock("VRCFaceTracking"),
        LogGate);

    public static MainWindow? MainWindow
    {
        get; private set;
    }

    public static T GetService<T>()
        where T : class
    {
        if (_host == null)
        {
            throw new InvalidOperationException($"{typeof(T).Name} requested before the host was built.");
        }

        if (_host.Services.GetService(typeof(T)) is not T service)
        {
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.axaml.cs.");
        }

        return service;
    }

    public override void Initialize()
    {
        _instance = this;
        LogGate.Set(BuildInfo.VerboseForced);
        var bootLogger = FileLog.CreateLogger("App");
        bootLogger.LogDebug("App constructing");
        CrashHandlers.Install(FileLog.CreateLogger("Crash"), FileLog.Flush);

        try
        {
            AvaloniaXamlLoader.Load(this);
        }
        catch (Exception e)
        {
            bootLogger.LogCritical(e, "App XAML initialization failed");
            FileLog.Flush();
            throw;
        }

        // gsettings breaks gamescope on linux and system theme is unreliable on plasma anyways
        if (!OperatingSystem.IsLinux())
        {
            var faTheme = Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
            if (faTheme is not null)
            {
                faTheme.PreferSystemTheme = true;
            }
        }

        HandleResetFile(bootLogger);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Remove duplicate Avalonia/CommunityToolkit data validation
        if (!_dataValidatorTrimmed)
        {
            BindingPlugins.DataValidators.RemoveAt(0);
            _dataValidatorTrimmed = true;
        }

        if (_host == null)
        {
            _host = Microsoft.Extensions.Hosting.Host
                .CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddDebug();
                    logging.AddConsole();
                    logging.AddProvider(new Services.Logging.OutputPageLogProvider(LogGate));
                    logging.AddProvider(FileLog);
                })
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureServices((context, services) => services.AddVrcftServices(context, LogGate, LoggingSettings, FileLog))
                .Build();
            Ioc.Default.ConfigureServices(_host.Services);
        }

        _logger = GetService<ILoggerFactory>().CreateLogger("App");
        _logger.LogDebug("Host built");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow = new MainWindow();
            desktop.MainWindow = MainWindow;
        }

        if (StartServices)
        {
            _ = LaunchAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task LaunchAsync()
    {
        try
        {
            await GetService<ILocalSettingsService>().Load(LoggingSettings);
            await GetService<ILocalSettingsService>().Load(GetService<UpdateSettings>());
            var developer = GetService<DeveloperSettings>();
            developer.Enabled = await GetService<ILocalSettingsService>().ReadSettingAsync(DeveloperSettings.SettingKey, developer.Enabled);

            var savedTheme = await GetService<ILocalSettingsService>().ReadSettingAsync<string?>(ViewModels.SettingsViewModel.ThemeSettingKey);
            if (!string.IsNullOrEmpty(savedTheme))
            {
                Dispatcher.UIThread.Post(() => RequestedThemeVariant = savedTheme switch
                {
                    "Light" => ThemeVariant.Light,
                    "Dark" => ThemeVariant.Dark,
                    _ => ThemeVariant.Default,
                });
            }
            LogGate.Set(LoggingSettings.VerboseEffective);
            FileLog.WriteRaw($"channel={BuildInfo.ChannelName} verbose={LogGate.Verbose} forced={BuildInfo.VerboseForced} keep={LoggingSettings.LogFilesToKeep}");
            LogRetention.Prune(Core.Utils.LogDirectory, LogFileNames.MainPattern, LoggingSettings.LogFilesToKeep);
            LogRetention.Prune(Core.Utils.LogDirectory, LogFileNames.ModulePattern, LoggingSettings.LogFilesToKeep);

            // Kill any other instances of VRCFaceTracking and our module processes
            Core.Utils.KillAllProcessesOfName("VRCFaceTracking");
            Core.Utils.KillAllProcessesOfName("VRCFaceTracking.ModuleProcess");

            var openVr = GetService<OpenVRService>();
            await GetService<ILocalSettingsService>().Load(openVr);
            openVr.QuitRequested += () => Dispatcher.UIThread.Post(() => _ = MainWindow?.CloseAfterTeardown());

            await GetService<IActivationService>().ActivateAsync(null!);
            await _host!.StartAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex, "App startup failed");
            FileLog.Flush();
        }
    }

    public static async Task StopHostAsync()
    {
        if (_host == null)
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _host.StopAsync(cts.Token);
        }
        catch (Exception ex)
        {
            _instance?._logger?.LogWarning(ex, "Host did not stop cleanly");
        }
    }

    private void HandleResetFile(ILogger bootLogger)
    {
        var resetFile = Path.Combine(Core.Utils.PersistentDataDirectory, "reset");
        if (!File.Exists(resetFile))
        {
            return;
        }

        try
        {
            File.Delete(resetFile);

            foreach (var file in Directory.EnumerateFiles(Core.Utils.PersistentDataDirectory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception e)
                {
                    bootLogger.LogWarning("Reset could not delete {File}: {Message}", file, e.Message);
                }
            }
        }
        catch (Exception e)
        {
            bootLogger.LogWarning(e, "Reset cleanup failed");
        }
    }
}
