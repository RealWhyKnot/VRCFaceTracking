using VRCFaceTracking.Core.Sandboxing.Ipc;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Library;

public sealed class CameraStreamRegistry
{
    public const int MaxStreams = 6;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(5);
    public const string LegacyEyeId = "eye";
    public const string LegacyLipId = "lip";

    private readonly Lock _lock = new();
    private readonly List<CameraStreamSnapshot> _streams = [];
    private readonly TimeProvider _time;
    private long _sequence;
    private int _version;

    public CameraStreamRegistry(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    public int Version => Volatile.Read(ref _version);

    public bool Update(Guid moduleId, string moduleName, ImageFrameV2Packet packet) =>
        Submit(moduleId, moduleName, packet.StreamId, packet.Label, packet.Role, packet.Format, packet.Width, packet.Height, packet.Capabilities, packet.GetData());

    public bool UpdateLegacy(Guid moduleId, string moduleName, byte kind, int width, int height, byte[] data)
    {
        bool eye = kind == ImageFrameUpdatePacket.EyeKind;
        return Submit(
            moduleId,
            moduleName,
            eye ? LegacyEyeId : LegacyLipId,
            eye ? "Eye" : "Lip",
            eye ? CameraStreamRole.EyeCombined : CameraStreamRole.Mouth,
            CameraPixelFormat.Rgba8888,
            width,
            height,
            CameraControlCapabilities.None,
            data);
    }

    public IReadOnlyList<CameraStreamSnapshot> Snapshot()
    {
        lock (_lock)
        {
            return [.. _streams];
        }
    }

    public void ClearModule(Guid moduleId)
    {
        lock (_lock)
        {
            if (_streams.RemoveAll(s => s.ModuleId == moduleId) > 0)
            {
                Interlocked.Increment(ref _version);
            }
        }
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            if (_streams.Count > 0)
            {
                _streams.Clear();
                Interlocked.Increment(ref _version);
            }
        }
    }

    private bool Submit(Guid moduleId, string moduleName, string streamId, string label, CameraStreamRole role, CameraPixelFormat format, int width, int height, CameraControlCapabilities capabilities, byte[] data)
    {
        if (streamId.Length == 0 || !ImageFrameV2Packet.IsValidFrame(format, width, height, data.Length))
        {
            return false;
        }

        lock (_lock)
        {
            int index = _streams.FindIndex(s => s.ModuleId == moduleId && string.Equals(s.StreamId, streamId, StringComparison.Ordinal));
            if (index < 0 && _streams.Count >= MaxStreams)
            {
                long now = _time.GetTimestamp();
                _streams.RemoveAll(s => _time.GetElapsedTime(s.TimestampTicks, now) >= StaleAfter);
                if (_streams.Count >= MaxStreams)
                {
                    return false;
                }
            }

            var snapshot = new CameraStreamSnapshot(moduleId, moduleName, streamId, label, role, format, width, height, capabilities, data, ++_sequence, _time.GetTimestamp());
            if (index < 0)
            {
                _streams.Add(snapshot);
            }
            else
            {
                _streams[index] = snapshot;
            }

            Interlocked.Increment(ref _version);
            return true;
        }
    }
}
