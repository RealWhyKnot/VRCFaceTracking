using System.Globalization;
using System.Runtime.InteropServices;

namespace VRCFaceTracking.Core.Params.Data;

[StructLayout(LayoutKind.Auto)]
public readonly record struct TrackingActivity(TrackingActivityState State, double SecondsSinceData, bool VrChatNativeFaceTrackingActive)
{
    public const double IdleAfterSeconds = 3;

    public bool ExternalEyes { get; init; }

    public bool ExternalFace { get; init; }

    public string Summary
    {
        get
        {
            string native = VrChatNativeFaceTrackingActive ? "VRChat native face tracking active" : string.Empty;
            string state = State switch
            {
                TrackingActivityState.NoData => "Waiting for tracking data",
                TrackingActivityState.Active => "Tracking active",
                TrackingActivityState.Idle => string.Create(CultureInfo.InvariantCulture, $"Idle, no new data for {SecondsSinceData:0} s"),
                _ => "Waiting for tracking data",
            };
            return native.Length == 0 ? state : state + "; " + native;
        }
    }

    public static TrackingActivity Classify(long lastDataTicks, long nowTicks, long ticksPerSecond, bool nativeFaceTracking)
    {
        if (lastDataTicks == 0)
        {
            return new TrackingActivity(TrackingActivityState.NoData, 0, nativeFaceTracking);
        }

        double seconds = Math.Max(0, (nowTicks - lastDataTicks) / (double)ticksPerSecond);
        TrackingActivityState state = seconds >= IdleAfterSeconds ? TrackingActivityState.Idle : TrackingActivityState.Active;
        return new TrackingActivity(state, seconds, nativeFaceTracking);
    }
}
