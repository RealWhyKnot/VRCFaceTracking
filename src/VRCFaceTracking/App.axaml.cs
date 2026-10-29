using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using FluentAvalonia.Styling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Debug;
using VRCFaceTracking.Contracts.Services;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.Updates;
using VRCFaceTracking.Services;
using VRCFaceTracking.Views;

namespace VRCFaceTracking;

internal sealed partial class App : Application
{
    private static App? Instance;
    private ILogger? _logger;
    private static IHost? AppHost;

    public static bool StartServices = true;
    public static bool EnableMotion = true;
    private static bool DataValidatorTrimmed;

    internal static readonly LogLevelGate LogGate = new();
    private static readonly LoggingSettings LoggingSettings = new();
    private static readonly FileLoggerProvider FileLog = new(
        LogDirectories.Current,
        LogFileNames.Main(DateTime.Now),
        BuildInfo.HeaderBlock("VRCFaceTracking", DateTimeOffset.UtcNow),
        LogGate,
        TimeProvider.System);

    public static MainWindow? MainWindow
    {
        get; private set;
    }

    public static T GetService<T>()
        where T : class
    {
        return AppHost == null
            ? throw new InvalidOperationException($"{typeof(T).Name} requested before the host was built.")
            : AppHost.Services.GetService(typeof(T)) is not T service
            ? throw new InvalidOperationException($"{typeof(T)} needs to be registered in ConfigureServices within App.axaml.cs.")
            : service;
    }

    public override void Initialize()
    {
        Instance = this;
        LogGate.Set(BuildInfo.VerboseForced);
        ILogger bootLogger = FileLog.CreateLogger("App");
        LogAppConstructing(bootLogger);
        ILogger crashLogger = FileLog.CreateLogger("Crash");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashHandlers.ReportUnhandled(crashLogger, e, FileLog.Flush);
        TaskScheduler.UnobservedTaskException += (_, e) => CrashHandlers.ReportUnobserved(crashLogger, e, FileLog.Flush);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => FileLog.Flush();
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            LogUnhandledUiThreadException(crashLogger, e.Exception);
            FileLog.Flush();
            e.Handled = true;
        };

        AvaloniaXamlLoader.Load(this);

        if (!EnableMotion)
        {
            StyleInclude? motion = Styles
                .OfType<StyleInclude>()
                .FirstOrDefault(s => s.Source?.ToString().EndsWith("Motion.axaml", StringComparison.Ordinal) == true);
            if (motion is not null)
            {
                Styles.Remove(motion);
            }
        }

        if (!OperatingSystem.IsLinux())
        {
            FluentAvaloniaTheme? faTheme = Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
            faTheme?.PreferSystemTheme = true;
        }

        HandleResetFile(bootLogger);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (!DataValidatorTrimmed)
        {
            BindingPlugins.DataValidators.RemoveAt(0);
            DataValidatorTrimmed = true;
        }

        if (AppHost == null)
        {
            AppHost = Host
                .CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddDebug();
                    logging.AddConsole();
                    logging.AddFilter<DebugLoggerProvider>(LogGate.IsEnabled);
                    logging.AddFilter<ConsoleLoggerProvider>(LogGate.IsEnabled);
                    logging.AddProvider(new Services.Logging.OutputPageLogProvider(LogGate));
                    logging.AddProvider(FileLog);
                })
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureServices((context, services) => services.AddVrcftServices(context, LogGate, LoggingSettings, FileLog))
                .Build();
            Ioc.Default.ConfigureServices(AppHost.Services);
        }

        _logger = GetService<ILoggerFactory>().CreateLogger("App");
        LogHostBuilt(_logger);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow = new MainWindow();
            desktop.MainWindow = MainWindow;
        }

        if (StartServices)
        {
            CancellationToken stopping = GetService<IHostApplicationLifetime>().ApplicationStopping;
            _ = LaunchAsync(stopping).ContinueWith(RethrowOnUiThread, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void RethrowOnUiThread(Task faulted) =>
        Dispatcher.UIThread.Post(() => ExceptionDispatchInfo.Throw(faulted.Exception!.InnerException!));

    private async Task LaunchAsync(CancellationToken cancellationToken)
    {
        ILocalSettingsService settings = GetService<ILocalSettingsService>();
        await settings.LoadAsync(LoggingSettings, cancellationToken);
        await settings.LoadAsync(GetService<UpdateSettings>(), cancellationToken);
        DeveloperSettings developer = GetService<DeveloperSettings>();
        developer.Enabled = await settings.ReadSettingAsync(DeveloperSettings.SettingKey, developer.Enabled, forceLocal: false, cancellationToken);
        developer.PropertyChanged += (_, _) =>
            _ = settings.SaveSettingAsync(DeveloperSettings.SettingKey, developer.Enabled, forceLocal: false, CancellationToken.None);

        string? savedTheme = await settings.ReadSettingAsync<string?>(ViewModels.SettingsViewModel.ThemeSettingKey, defaultValue: null, forceLocal: false, cancellationToken);
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
        LogRetention.Prune(LogDirectories.Current, LogFileNames.MainPattern, LoggingSettings.LogFilesToKeep);
        LogRetention.Prune(LogDirectories.Current, LogFileNames.ModulePattern, LoggingSettings.LogFilesToKeep);

        Core.Utils.KillAllProcessesOfName("VRCFaceTracking");
        Core.Utils.KillAllProcessesOfName("VRCFaceTracking.ModuleProcess");

        OpenVRService openVr = GetService<OpenVRService>();
        await settings.LoadAsync(openVr, cancellationToken);
        openVr.QuitRequested += (_, _) => Dispatcher.UIThread.Post(() => _ = MainWindow?.CloseAfterTeardownAsync(CancellationToken.None));

        await GetService<IActivationService>().ActivateAsync(cancellationToken);
        await AppHost!.StartAsync(cancellationToken);
    }

    public static async Task StopHostAsync(CancellationToken cancellationToken)
    {
        if (AppHost == null)
        {
            return;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            await AppHost.StopAsync(cts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or AggregateException)
        {
            ILogger? logger = Instance?._logger;
            if (logger is not null)
            {
                LogHostDidNotStopCleanly(logger, ex);
            }
        }
    }

    private static void HandleResetFile(ILogger bootLogger)
    {
        string resetFile = Path.Combine(Core.Utils.PersistentDataDirectory, "reset");
        if (!File.Exists(resetFile))
        {
            return;
        }

        try
        {
            File.Delete(resetFile);

            foreach (string file in Directory.EnumerateFiles(Core.Utils.PersistentDataDirectory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    LogResetCouldNotDelete(bootLogger, file, e.Message);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogResetCleanupFailed(bootLogger, e);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "App constructing")]
    private static partial void LogAppConstructing(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Host built")]
    private static partial void LogHostBuilt(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reset could not delete {File}: {Message}")]
    private static partial void LogResetCouldNotDelete(ILogger logger, string file, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reset cleanup failed")]
    private static partial void LogResetCleanupFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on the UI thread")]
    private static partial void LogUnhandledUiThreadException(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Host did not stop cleanly")]
    private static partial void LogHostDidNotStopCleanly(ILogger logger, Exception exception);
}
