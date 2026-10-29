using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VRCFaceTracking.Contracts.Services;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.Mdns;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.Core.Osc.Query;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Services;
using VRCFaceTracking.Core.Updates;
using VRCFaceTracking.Models;
using VRCFaceTracking.Services;
using VRCFaceTracking.ViewModels;

namespace VRCFaceTracking;

internal static class ServiceCollectionExtensions
{
    public static void AddVrcftServices(this IServiceCollection services, HostBuilderContext context,
        LogLevelGate logGate, LoggingSettings loggingSettings, FileLoggerProvider fileLog)
    {
        services.AddSingleton(logGate);
        services.AddSingleton(loggingSettings);
        services.AddSingleton(fileLog);

        AddAppServices(services);
        AddTrackingServices(services);
        AddViewModels(services);
        AddHostedServices(services);

        services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
    }

    private static void AddAppServices(IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
        services.AddSingleton<IActivationService, ActivationService>();
        services.AddSingleton<IDispatcherService, DispatcherService>();
        services.AddTransient<IIdentityService, IdentityService>();

        services.AddSingleton<ModuleInstaller>();
        services.AddSingleton<IModuleDataService, ModuleDataService>();
        services.AddTransient<IFileService, FileService>();
        services.AddSingleton<DeveloperSettings>();
        services.AddSingleton<UpdateSettings>();
        services.AddSingleton<UpdateService>();
    }

    private static void AddTrackingServices(IServiceCollection services)
    {
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
        services.AddSingleton<BinaryFaceDataSender>();
        services.AddSingleton<UnifiedTrackingMutator>();
    }

    private static void AddViewModels(IServiceCollection services)
    {
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<OutputViewModel>();
        services.AddSingleton<ModuleRegistryViewModel>();
        services.AddSingleton<MutatorViewModel>();
        services.AddSingleton<RiskySettingsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<ParameterViewModel>();
        services.AddTransient<ParametersViewModel>();
    }

    private static void AddHostedServices(IServiceCollection services)
    {
        services.AddHostedService(provider => provider.GetRequiredService<ParameterSenderService>());
        services.AddHostedService(provider => provider.GetRequiredService<BinaryFaceDataSender>());
        services.AddHostedService(provider => provider.GetRequiredService<OscRecvService>());
        services.AddHostedService(provider => provider.GetRequiredService<OscQueryService>());
    }
}
