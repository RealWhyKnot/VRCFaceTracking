using VRCFaceTracking.Core.Params.Data;

namespace VRCFaceTracking.Core.Contracts.Services;

public interface IExternalTrackingSource
{
    int Version { get; }

    WaitHandle DataArrived { get; }

    TimeSpan? SinceLastData { get; }

    bool DrivesEyes { get; }

    bool DrivesFace { get; }

    void Merge(UnifiedTrackingData data);
}
