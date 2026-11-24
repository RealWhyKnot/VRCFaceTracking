using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Services;

public sealed partial class ExternalOscInputService
{
    public void Merge(UnifiedTrackingData data)
    {
        if (Volatile.Read(ref _client) == null || !Settings.Enabled)
        {
            return;
        }

        SignalCapabilitySnapshot modules = _capabilities.Snapshot;
        long now = _time.GetTimestamp();
        bool[]? published = null;
        bool publishedEyeLeft;
        bool publishedEyeRight;
        int epoch;
        lock (_valuesLock)
        {
            bool changed = MergeShapesLocked(data, modules, now);
            bool eyeLeft = MergeEye(ref data.Eye.Left, ExternalOscEyeChannel.LeftX, ExternalOscEyeChannel.LeftY, ExternalOscEyeChannel.LeftOpenness, modules.ModuleEyeLeftLive, now);
            bool eyeRight = MergeEye(ref data.Eye.Right, ExternalOscEyeChannel.RightX, ExternalOscEyeChannel.RightY, ExternalOscEyeChannel.RightOpenness, modules.ModuleEyeRightLive, now);
            if (eyeLeft != _drivenEyeLeft || eyeRight != _drivenEyeRight)
            {
                _drivenEyeLeft = eyeLeft;
                _drivenEyeRight = eyeRight;
                changed = true;
            }

            epoch = Volatile.Read(ref _releaseEpoch);
            publishedEyeLeft = _drivenEyeLeft;
            publishedEyeRight = _drivenEyeRight;
            if (changed)
            {
                published = SnapshotDrivenLocked();
            }
        }

        if (published != null)
        {
            lock (_publishLock)
            {
                if (epoch == Volatile.Read(ref _releaseEpoch))
                {
                    _capabilities.SetExternal(published, publishedEyeLeft, publishedEyeRight);
                }
            }
        }
    }

    private bool MergeShapesLocked(UnifiedTrackingData data, SignalCapabilitySnapshot modules, long now)
    {
        bool changed = false;
        for (int i = 0; i < _shapes.Length; i++)
        {
            bool apply = _hasShape[i]
                && now - _shapeStamp[i] <= _staleTicks
                && !modules.IsModuleLive((UnifiedExpressions)i)
                && data.Shapes[i].Weight == 0f;
            if (apply)
            {
                data.Shapes[i].Weight = Math.Clamp(_shapes[i], 0f, 1f);
            }

            if (apply != _driven[i])
            {
                _driven[i] = apply;
                changed = true;
            }
        }

        return changed;
    }

    private bool MergeEye(ref UnifiedSingleEyeData eye, ExternalOscEyeChannel x, ExternalOscEyeChannel y, ExternalOscEyeChannel openness, bool moduleLive, long now)
    {
        if (moduleLive)
        {
            return false;
        }

        bool applied = false;
        if (eye.Gaze.x == 0f && eye.Gaze.y == 0f)
        {
            if (TryFreshEye(x, ExternalOscEyeChannel.CombinedX, now, out float gazeX))
            {
                eye.Gaze.x = gazeX;
                applied = true;
            }

            if (TryFreshEye(y, ExternalOscEyeChannel.CombinedY, now, out float gazeY))
            {
                eye.Gaze.y = gazeY;
                applied = true;
            }
        }

        if (eye.Openness == 1f && TryFreshEye(openness, openness, now, out float value))
        {
            eye.Openness = Math.Clamp(value, 0f, 1f);
            applied = true;
        }

        return applied;
    }

    private bool TryFreshEye(ExternalOscEyeChannel channel, ExternalOscEyeChannel fallback, long now, out float value)
    {
        if (IsFreshEye(channel, now))
        {
            value = _eye[(int)channel];
            return true;
        }

        if (IsFreshEye(fallback, now))
        {
            value = _eye[(int)fallback];
            return true;
        }

        value = 0f;
        return false;
    }

    private bool IsFreshEye(ExternalOscEyeChannel channel, long now) =>
        _hasEye[(int)channel] && now - _eyeStamp[(int)channel] <= _staleTicks;

    private bool[]? SnapshotDrivenLocked()
    {
        bool any = _drivenEyeLeft || _drivenEyeRight;
        bool[] shapes = (bool[])_driven.Clone();
        foreach (bool driven in shapes)
        {
            any |= driven;
        }

        if (!any && !_anyDriven)
        {
            return null;
        }

        _anyDriven = any;
        return shapes;
    }

    private void ReleaseDriven()
    {
        bool publish = false;
        lock (_valuesLock)
        {
            Interlocked.Increment(ref _releaseEpoch);
            Array.Clear(_driven);
            _drivenEyeLeft = false;
            _drivenEyeRight = false;
            if (_anyDriven)
            {
                _anyDriven = false;
                publish = true;
            }
        }

        if (publish)
        {
            lock (_publishLock)
            {
                _capabilities.SetExternal(new bool[_driven.Length], eyeLeft: false, eyeRight: false);
            }
        }
    }
}
