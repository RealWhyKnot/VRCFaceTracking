using System.Runtime.InteropServices;

namespace VRCFaceTracking.Core.Params.Data;

[StructLayout(LayoutKind.Auto)]
public readonly record struct TrackingPreviewSnapshot(TrackingPreviewValues Raw, TrackingPreviewValues Adjusted, bool MutationsApplied, long TimestampTicks);
