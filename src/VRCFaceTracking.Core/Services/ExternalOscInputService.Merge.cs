using System.Diagnostics;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Services;

public sealed partial class ExternalOscInputService
{
    private static readonly Dictionary<UnifiedExpressions, (ExternalOscEyeChannel Side, ExternalOscEyeChannel Both)> DerivedShapes = new()
    {
        [UnifiedExpressions.EyeWideLeft] = (ExternalOscEyeChannel.LeftWiden, ExternalOscEyeChannel.CombinedWiden),
        [UnifiedExpressions.EyeWideRight] = (ExternalOscEyeChannel.RightWiden, ExternalOscEyeChannel.CombinedWiden),
        [UnifiedExpressions.EyeSquintLeft] = (ExternalOscEyeChannel.LeftSquint, ExternalOscEyeChannel.CombinedSquint),
        [UnifiedExpressions.EyeSquintRight] = (ExternalOscEyeChannel.RightSquint, ExternalOscEyeChannel.CombinedSquint),
    };

    public void Merge(UnifiedTrackingData data)
    {
        if (Volatile.Read(ref _session) == null || !Settings.Enabled)
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
            bool changed = MergeShapesLocked(data, modules, now, out bool eyeShapes, out bool faceShapes);
            bool eyeLeft = MergeEye(ref data.Eye.Left, left: true, modules.ModuleEyeLeftLive, now);
            bool eyeRight = MergeEye(ref data.Eye.Right, left: false, modules.ModuleEyeRightLive, now);
            if (eyeLeft != _drivenEyeLeft || eyeRight != _drivenEyeRight)
            {
                _drivenEyeLeft = eyeLeft;
                _drivenEyeRight = eyeRight;
                changed = true;
            }

            _drivesEyes = eyeShapes || eyeLeft || eyeRight;
            _drivesFace = faceShapes;
            epoch = Volatile.Read(ref _releaseEpoch);
            publishedEyeLeft = _drivenEyeLeft;
            publishedEyeRight = _drivenEyeRight;
            if (changed)
            {
                published = SnapshotDrivenLocked();
            }
        }

        if (publishedEyeLeft || publishedEyeRight)
        {
            _capabilities.ObserveExternalEyes(data, Stopwatch.GetTimestamp());
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

    private static bool IsEyeShape(UnifiedExpressions shape) =>
        shape is UnifiedExpressions.EyeSquintLeft or UnifiedExpressions.EyeSquintRight or UnifiedExpressions.EyeWideLeft or UnifiedExpressions.EyeWideRight;

    private bool MergeShapesLocked(UnifiedTrackingData data, SignalCapabilitySnapshot modules, long now, out bool eyeShapes, out bool faceShapes)
    {
        bool changed = false;
        eyeShapes = false;
        faceShapes = false;
        for (int i = 0; i < _shapes.Length; i++)
        {
            var shape = (UnifiedExpressions)i;
            bool apply = TryFreshShape(shape, modules, now, out float value)
                && !modules.IsModuleLive(shape)
                && data.Shapes[i].Weight == 0f;
            if (apply)
            {
                data.Shapes[i].Weight = Math.Clamp(value, 0f, 1f);
                if (IsEyeShape(shape))
                {
                    eyeShapes = true;
                }
                else
                {
                    faceShapes = true;
                }
            }

            if (apply != _driven[i])
            {
                _driven[i] = apply;
                changed = true;
            }
        }

        return changed;
    }

    private bool TryFreshShape(UnifiedExpressions shape, SignalCapabilitySnapshot modules, long now, out float value)
    {
        int index = (int)shape;
        if (_hasShape[index] && now - _shapeStamp[index] <= _staleTicks)
        {
            value = _shapes[index];
            return true;
        }

        if (DerivedShapes.TryGetValue(shape, out (ExternalOscEyeChannel Side, ExternalOscEyeChannel Both) derived))
        {
            bool sideLive = shape is UnifiedExpressions.EyeWideLeft or UnifiedExpressions.EyeSquintLeft
                ? modules.ModuleEyeLeftLive
                : modules.ModuleEyeRightLive;
            if (sideLive)
            {
                value = 0f;
                return false;
            }

            return TryFreshEye(now, out value, derived.Side, derived.Both);
        }

        value = 0f;
        return false;
    }

    private bool MergeEye(ref UnifiedSingleEyeData eye, bool left, bool moduleLive, long now)
    {
        if (moduleLive)
        {
            return false;
        }

        bool applied = false;
        if (eye.Gaze.x == 0f && eye.Gaze.y == 0f)
        {
            if (TryFreshEye(now, out float gazeX, left ? ExternalOscEyeChannel.LeftX : ExternalOscEyeChannel.RightX, ExternalOscEyeChannel.CombinedX))
            {
                eye.Gaze.x = gazeX;
                applied = true;
            }

            if (TryFreshEye(now, out float gazeY, left ? ExternalOscEyeChannel.LeftY : ExternalOscEyeChannel.RightY, ExternalOscEyeChannel.CombinedY))
            {
                eye.Gaze.y = gazeY;
                applied = true;
            }
        }

        bool fresh = TryFreshEye(
            now,
            out float openness,
            left ? ExternalOscEyeChannel.LeftOpenness : ExternalOscEyeChannel.RightOpenness,
            left ? ExternalOscEyeChannel.LeftOpennessDerived : ExternalOscEyeChannel.RightOpennessDerived,
            ExternalOscEyeChannel.CombinedOpenness,
            ExternalOscEyeChannel.CombinedOpennessDerived);
        if (eye.Openness == 1f && fresh)
        {
            eye.Openness = Math.Clamp(openness, 0f, 1f);
            applied = true;
        }

        return applied;
    }

    private bool TryFreshEye(long now, out float value, params ReadOnlySpan<ExternalOscEyeChannel> channels)
    {
        foreach (ExternalOscEyeChannel channel in channels)
        {
            if (IsFreshEye(channel, now))
            {
                value = _eye[(int)channel];
                return true;
            }
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
            _drivesEyes = false;
            _drivesFace = false;
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
