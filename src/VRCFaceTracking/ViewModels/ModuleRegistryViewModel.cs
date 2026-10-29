using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.ViewModels;

internal sealed partial class ModuleRegistryViewModel : ObservableRecipient
{
    private readonly IModuleDataService _moduleDataService;
    [ObservableProperty] private InstallableTrackingModule? _selected;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _canInstall;
    [ObservableProperty] private bool _canUninstall;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _installButtonText = Strings.Resources.ModuleActionInstall;
    [ObservableProperty] private string _uninstallButtonText = Strings.Resources.ModuleActionUninstall;
    private bool _loading;

    public ObservableCollection<InstallableTrackingModule> ModuleInfos { get; } = [];
    public ObservableCollection<InstallableTrackingModule> FilteredModuleInfos { get; } = [];

    public ModuleRegistryViewModel(IModuleDataService moduleDataService)
    {
        _moduleDataService = moduleDataService;
        ModuleInfos.CollectionChanged += (_, _) => ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    partial void OnSelectedChanged(InstallableTrackingModule? value) => RefreshActionState();

    public void RefreshActionState()
    {
        InstallState? state = Selected?.InstallationState;
        CanInstall = state is not null and not InstallState.Installed;
        CanUninstall = state is InstallState.Installed or InstallState.Outdated;
        IsBusy = false;
        InstallButtonText = Strings.Resources.ModuleActionInstall;
        UninstallButtonText = Strings.Resources.ModuleActionUninstall;
    }

    private void ApplyFilter()
    {
        string? query = SearchQuery?.Trim();
        var desired = (string.IsNullOrEmpty(query)
            ? ModuleInfos
            : ModuleInfos.Where(m =>
                (m.ModuleName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (m.AuthorName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))).ToList();

        for (int i = FilteredModuleInfos.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(FilteredModuleInfos[i]))
            {
                FilteredModuleInfos.RemoveAt(i);
            }
        }

        for (int i = 0; i < desired.Count; i++)
        {
            InstallableTrackingModule module = desired[i];
            int current = FilteredModuleInfos.IndexOf(module);
            if (current < 0)
            {
                FilteredModuleInfos.Insert(i, module);
            }
            else if (current != i)
            {
                FilteredModuleInfos.Move(current, i);
            }
        }
    }

    public async Task LoadModulesAsync(CancellationToken cancellationToken)
    {
        if (_loading)
        {
            return;
        }
        _loading = true;

        try
        {
            ModuleInfos.Clear();

            IEnumerable<InstallableTrackingModule> data = await _moduleDataService.GetRemoteModulesAsync(cancellationToken);

            var remoteModules = data
                .OrderByDescending(x => string.Equals(x.AuthorName, "VRCFT Team", StringComparison.Ordinal))
                .ThenBy(x => x.ModuleName, StringComparer.Ordinal)
                .ToList();
            foreach (InstallableTrackingModule? module in remoteModules)
            {
                module.InstallationState = InstallState.NotInstalled;
                ModuleInfos.Add(module);
            }

            IEnumerable<InstallableTrackingModule> installedModules = _moduleDataService.GetInstalledModules().Concat(_moduleDataService.GetLegacyModules());
            foreach (InstallableTrackingModule? installedModule in installedModules)
            {
                InstallableTrackingModule? remoteModule = ModuleInfos.FirstOrDefault(x => x.ModuleId == installedModule.ModuleId);
                if (remoteModule == null)
                {
                    installedModule.InstallationState = InstallState.Installed;
                    ModuleInfos.Insert(0, installedModule);
                }
                else
                {
                    remoteModule.InstallationState = ModuleUpdates.IsUpdate(remoteModule, installedModule)
                        ? InstallState.Outdated
                        : InstallState.Installed;
                    ModuleInfos.Move(ModuleInfos.IndexOf(remoteModule), 0);
                }
            }
        }
        finally
        {
            _loading = false;
        }
    }
}
