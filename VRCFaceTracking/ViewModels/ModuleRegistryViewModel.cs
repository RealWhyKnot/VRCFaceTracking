using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Models;

namespace VRCFaceTracking.ViewModels;

public partial class ModuleRegistryViewModel : ObservableRecipient
{
    private readonly IModuleDataService _moduleDataService;
    [ObservableProperty] private InstallableTrackingModule? _selected;
    [ObservableProperty] private string _searchQuery = string.Empty;

    public ObservableCollection<InstallableTrackingModule> ModuleInfos { get; } = new();
    public ObservableCollection<InstallableTrackingModule> FilteredModuleInfos { get; } = new();

    public ModuleRegistryViewModel(IModuleDataService moduleDataService)
    {
        _moduleDataService = moduleDataService;
        ModuleInfos.CollectionChanged += (_, _) => ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredModuleInfos.Clear();
        var query = SearchQuery?.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? ModuleInfos
            : ModuleInfos.Where(m =>
                (m.ModuleName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (m.AuthorName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        foreach (var m in filtered)
        {
            FilteredModuleInfos.Add(m);
        }
    }

    public async Task OnNavigatedTo()
    {
        ModuleInfos.Clear();

        var data = await _moduleDataService.GetRemoteModules();
        var remoteModules = data
            .OrderByDescending(x => x.AuthorName == "VRCFT Team")
            .ThenBy(x => x.ModuleName)
            .ToList();
        foreach (var module in remoteModules)
        {
            module.InstallationState = InstallState.NotInstalled;
            ModuleInfos.Add(module);
        }

        var installedModules = _moduleDataService.GetInstalledModules().Concat(_moduleDataService.GetLegacyModules());
        foreach (var installedModule in installedModules)
        {
            var remoteModule = ModuleInfos.FirstOrDefault(x => x.ModuleId == installedModule.ModuleId);
            if (remoteModule == null)
            {
                installedModule.InstallationState = InstallState.Installed;
                ModuleInfos.Insert(0, installedModule);
            }
            else
            {
                remoteModule.InstallationState = remoteModule.Version != installedModule.Version
                    ? InstallState.Outdated
                    : InstallState.Installed;
                ModuleInfos.Move(ModuleInfos.IndexOf(remoteModule), 0);
            }
        }
    }
}
