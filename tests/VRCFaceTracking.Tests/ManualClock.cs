namespace VRCFaceTracking.Tests;

internal sealed class ManualClock : TimeProvider
{
    private long _ticks = 1;

    public override long TimestampFrequency => 1000;

    public override long GetTimestamp() => _ticks;

    public void Advance(TimeSpan span) => _ticks += (long)span.TotalMilliseconds;
}
