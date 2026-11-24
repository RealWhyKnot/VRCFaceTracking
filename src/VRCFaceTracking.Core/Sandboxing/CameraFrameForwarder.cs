using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Sandboxing.Ipc;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Sandboxing;

public sealed partial class CameraFrameForwarder
{
    public const int DefaultIntervalMs = 33;
    public const int MinIntervalMs = 16;

    private readonly ICameraStreamProvider _provider;
    private readonly CameraControlCapabilities _capabilities;
    private readonly Action<IpcPacket> _enqueue;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Dictionary<string, long> _lastSent = new(StringComparer.Ordinal);
    private readonly List<string> _evicted = [];
    private long _lastPoll;

    public CameraFrameForwarder(ICameraStreamProvider provider, CameraControlCapabilities capabilities, Action<IpcPacket> enqueue, TimeProvider? time = null, ILogger? logger = null)
    {
        _provider = provider;
        _capabilities = capabilities;
        _enqueue = enqueue;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    public bool IsFaulted { get; private set; }

    public int Tick()
    {
        if (IsFaulted)
        {
            return 0;
        }

        long now = _time.GetTimestamp();
        if (_lastPoll != 0 && _time.GetElapsedTime(_lastPoll, now).TotalMilliseconds < MinIntervalMs)
        {
            return 0;
        }

        _lastPoll = now;
        try
        {
            return Poll(now);
        }
        catch (Exception ex) when (CameraControlInbox.IsModuleFault(ex))
        {
            IsFaulted = true;
            _lastSent.Clear();
            LogCameraStreamsFailed(_logger, ex, _provider.GetType().Name);
            return 0;
        }
    }

    private static int IntervalFor(string streamId, IReadOnlyList<CameraStreamDescriptor?> descriptors)
    {
        foreach (CameraStreamDescriptor? descriptor in descriptors)
        {
            if (descriptor != null && string.Equals(descriptor.Id, streamId, StringComparison.Ordinal))
            {
                return Math.Max(descriptor.TargetFrameIntervalMs, MinIntervalMs);
            }
        }

        return DefaultIntervalMs;
    }

    private int Poll(long now)
    {
        IReadOnlyList<CameraFrame?>? frames = _provider.GetCameraFrames();
        if (frames == null || frames.Count == 0)
        {
            return 0;
        }

        IReadOnlyList<CameraStreamDescriptor?> descriptors = _provider.GetCameraStreams() ?? [];
        int sent = 0;
        foreach (CameraFrame? frame in frames)
        {
            if (sent >= CameraStreamRegistry.MaxStreams)
            {
                break;
            }

            if (frame != null && Forward(frame, frames, descriptors, now))
            {
                sent++;
            }
        }

        return sent;
    }

    private bool Forward(CameraFrame frame, IReadOnlyList<CameraFrame?> frames, IReadOnlyList<CameraStreamDescriptor?> descriptors, long now)
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

        if (!_lastSent.ContainsKey(frame.StreamId) && _lastSent.Count >= CameraStreamRegistry.MaxStreams && !EvictInactive(frames, descriptors, now))
        {
            return false;
        }

        _lastSent[frame.StreamId] = now;
        var packet = new ImageFrameV2Packet(frame.StreamId, frame.Label ?? string.Empty, frame.Role, frame.Format, frame.Width, frame.Height, _capabilities, frame.Data);
        _enqueue(new EncodedIpcPacket(IpcPacket.PacketType.DebugStreamFrameV2, packet.GetBytes()));
        return true;
    }

    private static bool Listed(string streamId, IReadOnlyList<CameraFrame?> frames, IReadOnlyList<CameraStreamDescriptor?> descriptors)
    {
        foreach (CameraFrame? frame in frames)
        {
            if (frame != null && string.Equals(frame.StreamId, streamId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (CameraStreamDescriptor? descriptor in descriptors)
        {
            if (descriptor != null && string.Equals(descriptor.Id, streamId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool EvictInactive(IReadOnlyList<CameraFrame?> frames, IReadOnlyList<CameraStreamDescriptor?> descriptors, long now)
    {
        _evicted.Clear();
        foreach ((string streamId, long last) in _lastSent)
        {
            if (_time.GetElapsedTime(last, now) >= CameraStreamRegistry.StaleAfter || !Listed(streamId, frames, descriptors))
            {
                _evicted.Add(streamId);
            }
        }

        foreach (string streamId in _evicted)
        {
            _lastSent.Remove(streamId);
        }

        _evicted.Clear();
        return _lastSent.Count < CameraStreamRegistry.MaxStreams;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Module} threw while sharing camera frames, so its camera preview is off until the module restarts")]
    private static partial void LogCameraStreamsFailed(ILogger logger, Exception exception, string module);
}
