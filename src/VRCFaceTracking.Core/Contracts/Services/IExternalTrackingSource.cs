using VRCFaceTracking.Core.Params.Data;

namespace VRCFaceTracking.Core.Contracts.Services;

public interface IExternalTrackingSource
{
    void Merge(UnifiedTrackingData data);
}
