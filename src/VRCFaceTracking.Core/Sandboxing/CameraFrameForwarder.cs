using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Sandboxing.Ipc;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Sandboxing;

public sealed class CameraFrameForwarder
{
    public const int DefaultIntervalMs = 33;
    public const int MinIntervalMs = 16;

    private readonly ICameraStreamProvider _provider;
    private readonly CameraControlCapabilities _capabilities;
    private readonly Action<IpcPacket> _enqueue;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, long> _lastSent = new(StringComparer.Ordinal);
    private long _lastPoll;

    public CameraFrameForwarder(ICameraStreamProvider provider, CameraControlCapabilities capabilities, Action<IpcPacket> enqueue, TimeProvider? time = null)
    {
        _provider = provider;
        _capabilities = capabilities;
        _enqueue = enqueue;
        _time = time ?? TimeProvider.System;
    }

    public int Tick()
    {
        long now = _time.GetTimestamp();
        if (_lastPoll != 0 && _time.GetElapsedTime(_lastPoll, now).TotalMilliseconds < MinIntervalMs)
        {
            return 0;
        }

        _lastPoll = now;
        IReadOnlyList<CameraFrame> frames = _provider.GetCameraFrames();
        if (frames.Count == 0)
        {
            return 0;
        }

        IReadOnlyList<CameraStreamDescriptor> descriptors = _provider.GetCameraStreams();
        int sent = 0;
        foreach (CameraFrame frame in frames)
        {
            if (sent >= CameraStreamRegistry.MaxStreams)
            {
                break;
            }

            if (Forward(frame, descriptors, now))
            {
                sent++;
            }
        }

        return sent;
    }

    private bool Forward(CameraFrame frame, IReadOnlyList<CameraStreamDescriptor> descriptors, long now)
    {
        if (string.IsNullOrEmpty(frame.StreamId) || frame.Data == null ||
            !ImageFrameV2Packet.IsValidFrame(frame.Format, frame.Width, frame.Height, frame.Data.Length))
        {
            return false;
        }

        int interval = IntervalFor(frame.StreamId, descriptors);
        if (_lastSent.TryGetValue(frame.StreamId, out long last) && _time.GetElapsedTime(last, now).TotalMilliseconds < interval)
        {
            return false;
        }

        if (!_lastSent.ContainsKey(frame.StreamId) && _lastSent.Count >= CameraStreamRegistry.MaxStreams)
        {
            return false;
        }

        _lastSent[frame.StreamId] = now;
        var packet = new ImageFrameV2Packet(frame.StreamId, frame.Label ?? string.Empty, frame.Role, frame.Format, frame.Width, frame.Height, _capabilities, frame.Data);
        _enqueue(new EncodedIpcPacket(IpcPacket.PacketType.DebugStreamFrameV2, packet.GetBytes()));
        return true;
    }

    private static int IntervalFor(string streamId, IReadOnlyList<CameraStreamDescriptor> descriptors)
    {
        foreach (CameraStreamDescriptor descriptor in descriptors)
        {
            if (string.Equals(descriptor.Id, streamId, StringComparison.Ordinal))
            {
                return Math.Max(descriptor.TargetFrameIntervalMs, MinIntervalMs);
            }
        }

        return DefaultIntervalMs;
    }
}
