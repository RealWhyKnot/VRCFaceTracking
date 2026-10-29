using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core.Library;

public sealed partial class UnifiedLibManager
{
    private async Task LoadCapabilityOverridesAsync(CancellationToken cancellationToken)
    {
        try
        {
            Dictionary<string, TrackingCapability>? stored = await _localSettingsService
                .ReadSettingAsync<Dictionary<string, TrackingCapability>>(CapabilityOverridesSettingKey, defaultValue: null, forceLocal: true, cancellationToken)
                .ConfigureAwait(false);
            lock (_overridesLock)
            {
                _capabilityOverrides = stored ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogFailedLoadModuleCapabilityOverrides(_logger, ex.Message);
        }
    }

    private TrackingCapability? StoredCapabilityOverride(string modulePath)
    {
        lock (_overridesLock)
        {
            return _capabilityOverrides.TryGetValue(modulePath, out TrackingCapability stored) ? stored : null;
        }
    }

    private void OnAllowedCapabilitiesChanged(ModuleRuntimeInfo module)
    {
        ModuleMetadataInternal info = module.ModuleInformation;
        string path = module.SandboxModulePath;
        Dictionary<string, TrackingCapability> snapshot;
        lock (_overridesLock)
        {
            if (info.AllowedCapabilities is { } allowed)
            {
                _capabilityOverrides[path] = allowed;
            }
            else
            {
                _capabilityOverrides.Remove(path);
            }
            snapshot = new Dictionary<string, TrackingCapability>(_capabilityOverrides, StringComparer.Ordinal);
        }

        _ = SaveCapabilityOverridesAsync(snapshot, CancellationToken.None);

        TrackingCapability allowedMask = info.AllowedCapabilities ?? TrackingCapability.All;
        if (((allowedMask & TrackingCapabilities.EyeHalf) != TrackingCapability.None && info.SupportedEye && !module.EyeInitialized) ||
            ((allowedMask & TrackingCapabilities.ExpressionHalf) != TrackingCapability.None && info.SupportedExpression && !module.ExpressionInitialized))
        {
            LogInitializedWithoutOneHalvesNow(_logger, module.ModuleClassName);
        }

        RecomputeCapabilityAssignments();
    }

    private async Task SaveCapabilityOverridesAsync(Dictionary<string, TrackingCapability> snapshot, CancellationToken cancellationToken)
    {
        try
        {
            await _localSettingsService.SaveSettingAsync(CapabilityOverridesSettingKey, snapshot, forceLocal: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogFailedSaveModuleCapabilityOverrides(_logger, ex.Message);
        }
    }

    private void RecomputeCapabilityAssignments() => _dispatcherService.Run(() =>
    {
        ModuleRuntimeInfo[] modules;
        lock (_modulesLock)
        {
            modules = [.. _availableSandboxModules.OrderBy(m => m.SpawnOrder)];
        }

        var participants = new List<ModuleRuntimeInfo>(modules.Length);
        var candidates = new List<CapabilityCandidate>(modules.Length);
        foreach (ModuleRuntimeInfo module in modules)
        {
            ModuleMetadataInternal info = module.ModuleInformation;
            bool participates = !module.TeardownRequested && info.Active && !info.Crashed &&
                               (module.EyeInitialized || module.ExpressionInitialized);
            if (participates)
            {
                participants.Add(module);
                candidates.Add(new CapabilityCandidate(module.EyeInitialized, module.ExpressionInitialized, info.AllowedCapabilities));
            }
            else
            {
                ApplyAssignment(info, TrackingCapability.None);
            }
        }

        TrackingCapability[] assigned = CapabilityAssigner.Assign(candidates);
        for (int i = 0; i < participants.Count; i++)
        {
            ApplyAssignment(participants[i].ModuleInformation, assigned[i]);
        }

        EyeStatus = participants.Any(m => m.EyeInitialized) ? ModuleState.Active : ModuleState.Uninitialized;
        ExpressionStatus = participants.Any(m => m.ExpressionInitialized) ? ModuleState.Active : ModuleState.Uninitialized;
    });

    private static void ApplyAssignment(ModuleMetadataInternal info, TrackingCapability assigned)
    {
        info.EffectiveCapabilities = assigned;
        info.UsingEye = (assigned & TrackingCapabilities.EyeHalf) != TrackingCapability.None;
        info.UsingExpression = (assigned & TrackingCapabilities.ExpressionHalf) != TrackingCapability.None;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load module capability overrides: {Message}")]
    private static partial void LogFailedLoadModuleCapabilityOverrides(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Module} was initialized without one of the halves now allowed; restart modules to activate it")]
    private static partial void LogInitializedWithoutOneHalvesNow(ILogger logger, string module);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to save module capability overrides: {Message}")]
    private static partial void LogFailedSaveModuleCapabilityOverrides(ILogger logger, string message);
}
