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
    private static readonly long StaleTicks = Stopwatch.Frequency / 2;

    private readonly Lock _gate = new();
    private readonly ShapeStats[] _windows = [new(), new()];
    private readonly ShapeStats[] _externalEyes = [new(), new()];
    private int _current;
    private int _externalCurrent;
    private long _windowStart;
    private long _externalWindowStart;
    private long _lastPublish;
    private long _lastModuleSample;
    private bool _moduleStale;
    private long _version;
    private SignalCapabilitySnapshot _snapshot = SignalCapabilitySnapshot.Empty;
    private bool[]? _external;
    private bool _externalEyeLeft;
    private bool _externalEyeRight;

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

            _lastModuleSample = nowTicks;
            bool resumed = _moduleStale;
            _moduleStale = false;
            _windows[_current].Add(data);
            if (nowTicks - _windowStart >= WindowTicks)
            {
                _current = 1 - _current;
                _windows[_current].Reset();
                _windowStart = nowTicks;
            }

            if (resumed || nowTicks - _lastPublish >= PublishTicks)
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

    public void Expire(long nowTicks)
    {
        SignalCapabilitySnapshot published;
        lock (_gate)
        {
            if (_moduleStale || _lastModuleSample == 0 || nowTicks - _lastModuleSample < StaleTicks)
            {
                return;
            }

            _moduleStale = true;
            _lastPublish = nowTicks;
            published = Build();
            Volatile.Write(ref _snapshot, published);
        }

        Published?.Invoke(this, new SignalCapabilityEventArgs(published));
    }

    public void ObserveExternalEyes(UnifiedTrackingData data, long nowTicks)
    {
        SignalCapabilitySnapshot? published = null;
        lock (_gate)
        {
            if (_externalWindowStart == 0)
            {
                _externalWindowStart = nowTicks;
            }

            _externalEyes[_externalCurrent].Add(data);
            if (nowTicks - _externalWindowStart >= WindowTicks)
            {
                _externalCurrent = 1 - _externalCurrent;
                _externalEyes[_externalCurrent].Reset();
                _externalWindowStart = nowTicks;
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

    public void SetExternal(bool[] shapes, bool eyeLeft, bool eyeRight)
    {
        SignalCapabilitySnapshot published;
        lock (_gate)
        {
            _external = shapes;
            _externalEyeLeft = eyeLeft;
            _externalEyeRight = eyeRight;
            if (!eyeLeft && !eyeRight)
            {
                ResetExternalEyes();
            }

            published = Build();
            Volatile.Write(ref _snapshot, published);
        }

        Published?.Invoke(this, new SignalCapabilityEventArgs(published));
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
            _lastModuleSample = 0;
            _moduleStale = false;
            empty = Build();
            Volatile.Write(ref _snapshot, empty);
        }

        Published?.Invoke(this, new SignalCapabilityEventArgs(empty));
    }

    private void ResetExternalEyes()
    {
        _externalEyes[0].Reset();
        _externalEyes[1].Reset();
        _externalCurrent = 0;
        _externalWindowStart = 0;
    }

    private SignalCapabilitySnapshot Build()
    {
        ShapeStats current = _windows[_current];
        ShapeStats previous = _windows[1 - _current];
        bool fresh = !_moduleStale;
        bool[] live = new bool[ShapeCount];
        for (int i = 0; i < ShapeCount; i++)
        {
            live[i] = fresh && (Moved(current, i) || Moved(previous, i));
        }

        int left = ShapeCount;
        int right = ShapeCount + 1;
        bool moduleLeft = fresh && (EyeMeasured(current, left) || EyeMeasured(previous, left));
        bool moduleRight = fresh && (EyeMeasured(current, right) || EyeMeasured(previous, right));
        return new SignalCapabilitySnapshot(
            live,
            moduleLeft,
            moduleRight,
            _externalEyeLeft && !moduleLeft ? ExternalResting(left) : Resting(current, previous, left),
            _externalEyeRight && !moduleRight ? ExternalResting(right) : Resting(current, previous, right),
            current.Samples + previous.Samples,
            ++_version,
            _external,
            _externalEyeLeft,
            _externalEyeRight);
    }

    private static bool Moved(ShapeStats stats, int channel) => stats.Count(channel) > 0 && !stats.IsZero(channel);

    private float ExternalResting(int channel) => Resting(_externalEyes[_externalCurrent], _externalEyes[1 - _externalCurrent], channel);

    private static bool EyeMeasured(ShapeStats stats, int channel) =>
        stats.Count(channel) > 0 && (stats.Min(channel) != DefaultOpenness || stats.Max(channel) != DefaultOpenness);

    private static float Resting(ShapeStats current, ShapeStats previous, int channel)
    {
        ShapeStats source = current.Count(channel) >= previous.Count(channel) ? current : previous;
        return source.Count(channel) == 0 ? DefaultOpenness : Math.Clamp(source.Percentile(channel, RestingFraction), 0f, DefaultOpenness);
    }
}
