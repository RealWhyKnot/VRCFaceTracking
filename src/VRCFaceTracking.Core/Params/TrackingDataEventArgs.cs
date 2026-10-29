using VRCFaceTracking.Core.Params.Data;

namespace VRCFaceTracking.Core.Params;

public sealed class TrackingDataEventArgs : EventArgs
{
    public TrackingDataEventArgs(UnifiedTrackingData data)
    {
        Data = data;
    }

    public UnifiedTrackingData Data { get; }
}
