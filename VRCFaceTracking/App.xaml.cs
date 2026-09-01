using System.Diagnostics;
using System.Reflection;
using System.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using VRCFaceTracking.Activation;
using VRCFaceTracking.Contracts.Services;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.mDNS;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.Core.OSC.Query.mDNS;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Services;
using VRCFaceTracking.Models;
using VRCFaceTracking.Services;
using VRCFaceTracking.ViewModels;
using VRCFaceTracking.Views;
using UnhandledExceptionEventArgs = Microsoft.UI.Xaml.UnhandledExceptionEventArgs;

namespace VRCFaceTracking;

// To learn more about WinUI 3, see https://docs.microsoft.com/windows/apps/winui/winui3/.
public partial class App : Application
{
    // The .NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    public IHost Host
    {
        get;
    }
    
    private static App? _instance;
    private ILogger? _logger;

    private static readonly LogLevelGate LogGate = new();
    private static readonly LoggingSettings LoggingSettings = new();
    private static readonly FileLoggerProvider FileLog = new(
        Core.Utils.LogDirectory,
        LogFileNames.Main(DateTime.Now),
        BuildInfo.HeaderBlock("VRCFaceTracking"),
        LogGate);

    public static T GetService<T>()
        where T : class
    {
        if (_instance?.Host == null)
        {
            throw new InvalidOperationException($"{typeof(T).Name} requested before the host was built.");
        }

        if (_instance.Host.Services.GetService(typeof(T)) is not T service)
        {
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
        }

        return service;
    }

    private static WindowEx? _mainWindow;

    public static WindowEx MainWindow => _mainWindow ??= new MainWindow();

    public App()
    {
        _instance = this;
        LogGate.Set(BuildInfo.VerboseForced);
        var bootLogger = FileLog.CreateLogger("App");
        bootLogger.LogDebug("App constructing");
        CrashHandlers.Install(FileLog.CreateLogger("Crash"), FileLog.Flush);
        UnhandledException += ExceptionHandler;
        try
        {
            InitializeComponent();
        }
        catch (Exception e)
        {
            bootLogger.LogCritical(e, "App XAML initialization failed");
            FileLog.Flush();
            throw;
        }
        
        // Check for a "reset" file in the root of the app directory. If one is found, wipe all files from inside it
        // and delete the file.
        var resetFile = Path.Combine(VRCFaceTracking.Core.Utils.PersistentDataDirectory, "reset");
        if (File.Exists(resetFile))
        {
            // Delete everything including files and folders in Utils.PersistentDataDirectory
            foreach (var file in Directory.EnumerateFiles(VRCFaceTracking.Core.Utils.PersistentDataDirectory, "*", SearchOption.AllDirectories))
            {
                File.Delete(file);
            }
        }


        Host = Microsoft.Extensions.Hosting.Host.
        CreateDefaultBuilder().
        ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddDebug();
            logging.AddConsole();
            logging.AddProvider(new OutputLogProvider(DispatcherQueue.GetForCurrentThread(), LogGate));
            logging.AddProvider(FileLog);
        }).
        UseContentRoot(AppContext.BaseDirectory).
        ConfigureServices((context, services) =>
        {
            // Default Activation Handler
            services.AddTransient<ActivationHandler<LaunchActivatedEventArgs>, DefaultActivationHandler>();

            services.AddSingleton(LogGate);
            services.AddSingleton(LoggingSettings);
            services.AddSingleton(FileLog);
            services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
            services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
            services.AddTransient<INavigationViewService, NavigationViewService>();

            services.AddSingleton<IActivationService, ActivationService>();
            services.AddSingleton<IPageService, PageService>();
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IDispatcherService, DispatcherService>();

            // Core Services
            services.AddTransient<IIdentityService, IdentityService>();
            services.AddSingleton<ModuleInstaller>();
            services.AddSingleton<IModuleDataService, ModuleDataService>();
            services.AddTransient<IFileService, FileService>();
            services.AddSingleton<OscQueryService>();
            services.AddSingleton<MulticastDnsService>();
            services.AddSingleton<IMainService, MainStandalone>();
            services.AddTransient<AvatarConfigParser>();
            services.AddTransient<OscQueryConfigParser>();
            services.AddSingleton<UnifiedTracking>();
            services.AddSingleton<ILibManager, UnifiedLibManager>();
            services.AddSingleton<OpenVRService>();
            services.AddSingleton<IOscTarget, OscTarget>();
            services.AddSingleton<HttpHandler>();
            services.AddSingleton<OscSendService>();
            services.AddSingleton<OscRecvService>();
            services.AddSingleton<ParameterSenderService>();
            services.AddSingleton<UnifiedTrackingMutator>();
            services.AddTransient<GithubService>();

            // Views and ViewModels
            services.AddTransient<ModuleRegistryViewModel>();
            services.AddTransient<ModuleRegistryPage>();
            services.AddTransient<ParameterViewModel>();
            services.AddTransient<ParametersViewModel>();
            services.AddTransient<ParametersPage>();
            services.AddTransient<MutatorViewModel>();
            services.AddTransient<MutatorPage>();
            services.AddTransient<OutputViewModel>();
            services.AddTransient<OutputPage>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<RiskySettingsViewModel>();
            services.AddTransient<SettingsPage>();
            services.AddTransient<MainViewModel>();
            services.AddTransient<MainPage>();
            services.AddTransient<ShellPage>();
            services.AddTransient<ShellViewModel>();
            
            services.AddHostedService<ParameterSenderService>(provider => provider.GetService<ParameterSenderService>());
            services.AddHostedService<OscRecvService>(provider => provider.GetService<OscRecvService>());

            // Configuration
            services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
        }).
        Build();
        
        var logBuilder = App.GetService<ILoggerFactory>();
        _logger = logBuilder.CreateLogger("App");
        _logger.LogDebug("Host built");
    }

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);
        _logger?.LogDebug("OnLaunched");

        await App.GetService<ILocalSettingsService>().Load(LoggingSettings);
        LogGate.Set(LoggingSettings.VerboseEffective);
        FileLog.WriteRaw($"channel={BuildInfo.ChannelName} verbose={LogGate.Verbose} forced={BuildInfo.VerboseForced} keep={LoggingSettings.LogFilesToKeep}");
        LogRetention.Prune(Core.Utils.LogDirectory, LogFileNames.MainPattern, LoggingSettings.LogFilesToKeep);
        LogRetention.Prune(Core.Utils.LogDirectory, LogFileNames.ModulePattern, LoggingSettings.LogFilesToKeep);

        // Kill any other instances of VRCFaceTracking.exe and our module processes
        Core.Utils.KillAllProcessesOfName("VRCFaceTracking");
        Core.Utils.KillAllProcessesOfName("VRCFaceTracking.ModuleProcess");
        
        await App.GetService<IActivationService>().ActivateAsync(args);
        await Host.StartAsync();
    }
    
    [SecurityCritical]
    internal void ExceptionHandler(object sender, UnhandledExceptionEventArgs e)
    {
        // We need to hold the reference, because the Exception property is cleared when accessed.
        var exception = e.Exception;
        if (exception != null)
        {
            _logger?.LogCritical(exception, "Unhandled exception in UI thread");
            FileLog.Flush();
        }
    }

    public static TEnum GetEnum<TEnum>(string text) where TEnum : struct
    {
        if (!typeof(TEnum).GetTypeInfo().IsEnum)
        {
            throw new InvalidOperationException("Generic parameter 'TEnum' must be an enum.");
        }
        return (TEnum)Enum.Parse(typeof(TEnum), text);
    }
}
