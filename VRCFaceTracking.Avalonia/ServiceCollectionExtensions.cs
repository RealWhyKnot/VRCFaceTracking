using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
using VRCFaceTracking.Core.Updates;
using VRCFaceTracking.Models;
using VRCFaceTracking.Services;
using VRCFaceTracking.Services.Logging;
using VRCFaceTracking.ViewModels;

namespace VRCFaceTracking;

public static class ServiceCollectionExtensions
{
    public static void AddVrcftServices(this IServiceCollection services, HostBuilderContext context,
        LogLevelGate logGate, LoggingSettings loggingSettings, FileLoggerProvider fileLog)
    {
        services.AddSingleton(logGate);
        services.AddSingleton(loggingSettings);
        services.AddSingleton(fileLog);

        services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
        services.AddSingleton<IActivationService, ActivationService>();
        services.AddSingleton<IDispatcherService, DispatcherService>();
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
        services.AddSingleton<DeveloperSettings>();
        services.AddSingleton<UpdateSettings>();
        services.AddSingleton<UpdateService>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<OutputViewModel>();
        services.AddSingleton<ModuleRegistryViewModel>();
        services.AddSingleton<MutatorViewModel>();
        services.AddSingleton<RiskySettingsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<ParameterViewModel>();
        services.AddTransient<ParametersViewModel>();

        services.AddHostedService<ParameterSenderService>(provider => provider.GetService<ParameterSenderService>());
        services.AddHostedService<OscRecvService>(provider => provider.GetService<OscRecvService>());

        services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
    }
}
