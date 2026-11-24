using System.Diagnostics;
using VRCFaceTracking.Core.Logging;

namespace VRCFaceTracking.Core.Params.Data;

public sealed class SignalCapabilityTracker
{
    private const int WindowSeconds = 10;
    private const double RestingFraction = 0.5;
    private const float DefaultOpenness = 1f;

    private static readonly int ShapeCount = ShapeStats.ShapeCount;
    private static readonly long PublishTicks = Stopwatch.Frequency;
    private static readonly long WindowTicks = Stopwatch.Frequency * WindowSeconds;

    private readonly Lock _gate = new();
    private readonly ShapeStats[] _windows = [new(), new()];
    private int _current;
    private long _windowStart;
    private long _lastPublish;
    private long _version;
    private SignalCapabilitySnapshot _snapshot = SignalCapabilitySnapshot.Empty;

    public static SignalCapabilityTracker Shared { get; } = new();

    public event EventHandler<SignalCapabilityEventArgs>? Published;

    public SignalCapabilitySnapshot Snapshot => Volatile.Read(ref _snapshot);

    public void Observe(UnifiedTrackingData data, long nowTicks)
    {
        SignalCapabilitySnapshot? published = null;
        lock (_gate)
        {
            if (_windowStart == 0)
            {
                _windowStart = nowTicks;
                _lastPublish = nowTicks;
            }

            _windows[_current].Add(data);
            if (nowTicks - _windowStart >= WindowTicks)
            {
                _current = 1 - _current;
                _windows[_current].Reset();
                _windowStart = nowTicks;
            }

            if (nowTicks - _lastPublish >= PublishTicks)
            {
                _lastPublish = nowTicks;
                published = Build();
                Volatile.Write(ref _snapshot, published);
            }
        }

        if (published is not null)
        {
            Published?.Invoke(this, new SignalCapabilityEventArgs(published));
        }
    }

    public void Reset()
    {
        SignalCapabilitySnapshot empty;
        lock (_gate)
        {
            _windows[0].Reset();
            _windows[1].Reset();
            _current = 0;
            _windowStart = 0;
            _lastPublish = 0;
            empty = new SignalCapabilitySnapshot(new bool[ShapeCount], eyeLeftLive: false, eyeRightLive: false, DefaultOpenness, DefaultOpenness, sampleCount: 0, ++_version);
            Volatile.Write(ref _snapshot, empty);
        }

        Published?.Invoke(this, new SignalCapabilityEventArgs(empty));
    }

    private SignalCapabilitySnapshot Build()
    {
        ShapeStats current = _windows[_current];
        ShapeStats previous = _windows[1 - _current];
        bool[] live = new bool[ShapeCount];
        for (int i = 0; i < ShapeCount; i++)
        {
            live[i] = Moved(current, i) || Moved(previous, i);
        }

        int left = ShapeCount;
        int right = ShapeCount + 1;
        return new SignalCapabilitySnapshot(
            live,
            EyeMeasured(current, left) || EyeMeasured(previous, left),
            EyeMeasured(current, right) || EyeMeasured(previous, right),
            Resting(current, previous, left),
            Resting(current, previous, right),
            current.Samples + previous.Samples,
            ++_version);
    }

    private static bool Moved(ShapeStats stats, int channel) => stats.Count(channel) > 0 && !stats.IsZero(channel);

    private static bool EyeMeasured(ShapeStats stats, int channel) =>
        stats.Count(channel) > 0 && (stats.Min(channel) != DefaultOpenness || stats.Max(channel) != DefaultOpenness);

    private static float Resting(ShapeStats current, ShapeStats previous, int channel)
    {
        ShapeStats source = current.Count(channel) >= previous.Count(channel) ? current : previous;
        return source.Count(channel) == 0 ? DefaultOpenness : Math.Clamp(source.Percentile(channel, RestingFraction), 0f, DefaultOpenness);
    }
}
