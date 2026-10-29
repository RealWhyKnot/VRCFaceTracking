using VRCFaceTracking.Core.Models;

namespace VRCFaceTracking.Core.Contracts.Services;

public interface IModuleDataService
{
    Task<IEnumerable<InstallableTrackingModule>> GetRemoteModulesAsync(CancellationToken cancellationToken);
    Task<int?> GetMyRatingAsync(TrackingModuleMetadata moduleMetadata, CancellationToken cancellationToken);
    Task SetMyRatingAsync(TrackingModuleMetadata moduleMetadata, int rating, CancellationToken cancellationToken);
    IEnumerable<InstallableTrackingModule> GetInstalledModules();
    Task IncrementDownloadsAsync(TrackingModuleMetadata moduleMetadata, CancellationToken cancellationToken);
    IEnumerable<InstallableTrackingModule> GetLegacyModules();
}
