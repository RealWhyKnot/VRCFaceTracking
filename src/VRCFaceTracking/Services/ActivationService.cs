using System.Reflection;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Contracts.Services;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Services;

internal sealed partial class ActivationService : IActivationService
{
    private readonly OscQueryService _parameterOutputService;
    private readonly IMainService _mainService;
    private readonly IModuleDataService _moduleDataService;
    private readonly ModuleInstaller _moduleInstaller;
    private readonly ILibManager _libManager;
    private readonly ILogger<ActivationService> _logger;
    private readonly OpenVRService _openVrService;
    private readonly UpdateService _updateService;

    public ActivationService(
        OscQueryService parameterOutputService,
        IMainService mainService,
        IModuleDataService moduleDataService,
        ModuleInstaller moduleInstaller,
        ILibManager libManager,
        ILogger<ActivationService> logger,
        OpenVRService openVrService,
        UpdateService updateService)
    {
        _parameterOutputService = parameterOutputService;
        _mainService = mainService;
        _moduleDataService = moduleDataService;
        _moduleInstaller = moduleInstaller;
        _libManager = libManager;
        _logger = logger;
        _openVrService = openVrService;
        _updateService = updateService;
    }

    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            LogVrcftVersionInitializing(_logger, version);
        }

        LogInitializingOsc(_logger);
        await _parameterOutputService.InitializeAsync(cancellationToken).ConfigureAwait(false);

        LogInitializingMainService(_logger);
        await _mainService.InitializeAsync(cancellationToken).ConfigureAwait(false);

        LogInitializingOpenVR(_logger);
        if (!_openVrService.Initialize())
        {
            LogFailedInitializeOpenVRDuringActivationService(_logger);
        }
        _openVrService.StartReconnectLoop();

        LogCheckingDeletionRequestsInstalledModules(_logger);
        IEnumerable<InstallableTrackingModule> needsDeleting = _moduleDataService.GetInstalledModules().Concat(_moduleDataService.GetLegacyModules())
            .Where(m => m.InstallationState == InstallState.AwaitingRestart);
        foreach (InstallableTrackingModule? deleteModule in needsDeleting)
        {
            _moduleInstaller.UninstallModule(deleteModule);
        }

        await UpdateInstalledModulesAsync(cancellationToken).ConfigureAwait(false);

        LogInitializingModules(_logger);
        Dispatcher.UIThread.Post(_libManager.Initialize);

        _ = _updateService.CheckOnStartupAsync(CancellationToken.None);
    }

    private async Task UpdateInstalledModulesAsync(CancellationToken cancellationToken)
    {
        LogCheckingUpdatesInstalledModules(_logger);
        List<InstallableTrackingModule> localModules = [.. _moduleDataService.GetInstalledModules().Where(m => m.ModuleId != Guid.Empty)];
        IEnumerable<InstallableTrackingModule> remoteModules = await _moduleDataService.GetRemoteModulesAsync(cancellationToken).ConfigureAwait(false);
        IEnumerable<InstallableTrackingModule> outdatedModules = remoteModules.Where(rm => localModules.Exists(lm => ModuleUpdates.IsUpdate(rm, lm)));
        foreach (InstallableTrackingModule? outdatedModule in outdatedModules)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                string installedVersion = localModules.First(rm => rm.ModuleId == outdatedModule.ModuleId).Version;
                LogUpdating(_logger, outdatedModule.ModuleName, installedVersion, outdatedModule.Version);
            }
            await _moduleInstaller.InstallRemoteModuleAsync(outdatedModule, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "VRCFT Version {Version} initializing...")]
    private static partial void LogVrcftVersionInitializing(ILogger logger, Version? version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Initializing OSC...")]
    private static partial void LogInitializingOsc(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Initializing main service...")]
    private static partial void LogInitializingMainService(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Initializing OpenVR...")]
    private static partial void LogInitializingOpenVR(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to initialize OpenVR during ActivationService startup. Will keep retrying in the background.")]
    private static partial void LogFailedInitializeOpenVRDuringActivationService(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Checking for deletion requests for installed modules...")]
    private static partial void LogCheckingDeletionRequestsInstalledModules(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Checking for updates for installed modules...")]
    private static partial void LogCheckingUpdatesInstalledModules(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updating {ModuleName} from {FirstVersion} to {OutdatedModuleVersion}")]
    private static partial void LogUpdating(ILogger logger, string moduleName, string firstVersion, string outdatedModuleVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Initializing modules...")]
    private static partial void LogInitializingModules(ILogger logger);
}
