using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.ViewModels;

namespace VRCFaceTracking.UiTests;

public class ModuleRegistryViewModelTests
{
    private static readonly Guid ModuleId = Guid.Parse("5d2b8f0e-7c1a-4e3b-9a6d-0f4c2e8b1a37");

    private sealed class FakeModuleData(InstallableTrackingModule remote, InstallableTrackingModule installed) : IModuleDataService
    {
        public Task<IEnumerable<InstallableTrackingModule>> GetRemoteModules() => Task.FromResult<IEnumerable<InstallableTrackingModule>>(new[] { remote });
        public IEnumerable<InstallableTrackingModule> GetInstalledModules() => new[] { installed };
        public IEnumerable<InstallableTrackingModule> GetLegacyModules() => Array.Empty<InstallableTrackingModule>();
        public Task<int?> GetMyRatingAsync(TrackingModuleMetadata moduleMetadata) => throw new NotSupportedException();
        public Task SetMyRatingAsync(TrackingModuleMetadata moduleMetadata, int rating) => throw new NotSupportedException();
        public Task IncrementDownloadsAsync(TrackingModuleMetadata moduleMetadata) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("1.5", false, "1.6", InstallState.Outdated)]
    [InlineData("1.5", false, "1.5", InstallState.Installed)]
    [InlineData("1.6", false, "1.5", InstallState.Installed)]
    [InlineData("2026.9.27.0", true, "1.5", InstallState.Installed)]
    [InlineData("2026.9.27.0", false, "1.5", InstallState.Installed)]
    [InlineData("1.5", true, "1.6", InstallState.Installed)]
    [InlineData("alpha", false, "beta", InstallState.Outdated)]
    public async Task MarksOutdatedOnlyForANewerRegistryVersion(string installedVersion, bool isLocal, string remoteVersion, InstallState expected)
    {
        var remote = new InstallableTrackingModule { ModuleId = ModuleId, ModuleName = "Virtual Desktop", Version = remoteVersion };
        var installed = new InstallableTrackingModule { ModuleId = ModuleId, ModuleName = "Virtual Desktop", Version = installedVersion, IsLocal = isLocal };
        var viewModel = new ModuleRegistryViewModel(new FakeModuleData(remote, installed));

        await viewModel.OnNavigatedTo();

        var shown = Assert.Single(viewModel.ModuleInfos);
        Assert.Same(remote, shown);
        Assert.Equal(expected, shown.InstallationState);
    }
}
