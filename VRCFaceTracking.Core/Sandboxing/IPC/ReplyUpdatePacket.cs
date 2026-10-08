using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Sandboxing.IPC;

public class ReplyUpdatePacket : IpcPacket
{
    const int EXPRESSION_COUNT = (int)UnifiedExpressions.Max + 1;
    const float INVALID_FLOAT = 0xFFFFFFFF;

    private const int MaxDilation = 0;
    private const int MinDilation = 1;
    private const int LeftGazeX = 2;
    private const int LeftGazeY = 3;
    private const int LeftPupilDiameter = 4;
    private const int LeftOpenness = 5;
    private const int RightGazeX = 6;
    private const int RightGazeY = 7;
    private const int RightPupilDiameter = 8;
    private const int RightOpenness = 9;
    private const int HeadYaw = 10;
    private const int HeadPitch = 11;
    private const int HeadRoll = 12;
    private const int HeadPosX = 13;
    private const int HeadPosY = 14;
    private const int HeadPosZ = 15;

    private const int ScalarCount = 16;
    public const int PayloadSize = (ScalarCount + EXPRESSION_COUNT) * sizeof(float);

    private const int WirePayloadOffset = 12;

    private readonly byte[]? _payload;
    private byte[]? _wire;

    private const int StampCount = 2;
    public long SampleTicks;
    public long ReplySentTicks;

    internal static long NonFiniteRejected;

    public ReplyUpdatePacket()
    {
    }

    private ReplyUpdatePacket(byte[] payload, long sampleTicks)
    {
        _payload = payload;
        SampleTicks = sampleTicks;
    }

    public override PacketType GetPacketType() => PacketType.ReplyUpdate;

    public static ReplyUpdatePacket Capture(UnifiedTrackingData source, long sampleTicks)
    {
        var payload = new byte[PayloadSize];
        WritePayload(source, payload);
        return new ReplyUpdatePacket(payload, sampleTicks);
    }

    public static ReplyUpdatePacket? CaptureIfChanged(UnifiedTrackingData source, long sampleTicks, byte[] scratch, ReplyUpdatePacket? previous)
    {
        WritePayload(source, scratch);
        if (previous?._payload != null && scratch.AsSpan(0, PayloadSize).SequenceEqual(previous._payload))
        {
            return null;
        }
        return new ReplyUpdatePacket(scratch.AsSpan(0, PayloadSize).ToArray(), sampleTicks);
    }

    private static void WritePayload(UnifiedTrackingData source, byte[] destination)
    {
        var values = MemoryMarshal.Cast<byte, float>(destination.AsSpan(0, PayloadSize));
        values[MaxDilation] = source.Eye._maxDilation;
        values[MinDilation] = source.Eye._minDilation;
        values[LeftGazeX] = source.Eye.Left.Gaze.x;
        values[LeftGazeY] = source.Eye.Left.Gaze.y;
        values[LeftPupilDiameter] = source.Eye.Left.PupilDiameter_MM;
        values[LeftOpenness] = source.Eye.Left.Openness;
        values[RightGazeX] = source.Eye.Right.Gaze.x;
        values[RightGazeY] = source.Eye.Right.Gaze.y;
        values[RightPupilDiameter] = source.Eye.Right.PupilDiameter_MM;
        values[RightOpenness] = source.Eye.Right.Openness;
        values[HeadYaw] = source.Head.HeadYaw;
        values[HeadPitch] = source.Head.HeadPitch;
        values[HeadRoll] = source.Head.HeadRoll;
        values[HeadPosX] = source.Head.HeadPosX;
        values[HeadPosY] = source.Head.HeadPosY;
        values[HeadPosZ] = source.Head.HeadPosZ;
        for (var i = 0; i < EXPRESSION_COUNT; i++)
        {
            values[ScalarCount + i] = source.Shapes[i].Weight;
        }
    }

    public override byte[] GetBytes()
    {
        if (_payload == null)
        {
            throw new InvalidOperationException("No tracking data was captured into this packet");
        }

        var bytes = new byte[SIZE_PACKET_MAGIC + SIZE_PACKET_TYPE + sizeof(int) + _payload.Length + StampCount * sizeof(long)];
        Buffer.BlockCopy(HANDSHAKE_MAGIC, 0, bytes, 0, SIZE_PACKET_MAGIC);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), (uint)GetPacketType());
        BitConverter.TryWriteBytes(bytes.AsSpan(8), _payload.Length);
        Buffer.BlockCopy(_payload, 0, bytes, WirePayloadOffset, _payload.Length);

        var stamps = bytes.AsSpan(WirePayloadOffset + _payload.Length);
        BitConverter.TryWriteBytes(stamps, SampleTicks);
        BitConverter.TryWriteBytes(stamps.Slice(8), System.Diagnostics.Stopwatch.GetTimestamp());
        return bytes;
    }

    public override void Decode(in byte[] data)
    {
        _wire = null;
        if (data.Length < WirePayloadOffset)
        {
            return;
        }

        var structSize = BitConverter.ToInt32(data, 8);
        if (structSize != PayloadSize || data.Length < WirePayloadOffset + structSize)
        {
            return;
        }

        _wire = data;
        var stampsOffset = WirePayloadOffset + structSize;
        if (data.Length >= stampsOffset + StampCount * sizeof(long))
        {
            SampleTicks = BitConverter.ToInt64(data, stampsOffset);
            ReplySentTicks = BitConverter.ToInt64(data, stampsOffset + 8);
        }
        else
        {
            SampleTicks = ReplySentTicks = 0;
        }
    }

    private ReadOnlySpan<float> Values =>
        _wire != null ? MemoryMarshal.Cast<byte, float>(_wire.AsSpan(WirePayloadOffset, PayloadSize))
        : _payload != null ? MemoryMarshal.Cast<byte, float>(_payload.AsSpan(0, PayloadSize))
        : ReadOnlySpan<float>.Empty;

    internal static bool IsValid(float value)
    {
        if (value == INVALID_FLOAT)
        {
            return false;
        }
        if (float.IsFinite(value))
        {
            return true;
        }
        Interlocked.Increment(ref NonFiniteRejected);
        return false;
    }

    public void UpdateGlobalState(TrackingCapability allowed)
    {
        var values = Values;
        if (values.IsEmpty)
        {
            return;
        }

        if (allowed.HasFlag(TrackingCapability.Eyes))
        {
            UpdateEyeState(values);
        }
        if (allowed.HasFlag(TrackingCapability.Head))
        {
            UpdateHeadState(values);
        }

        var shapes = values.Slice(ScalarCount);
        foreach (var (capability, start, endInclusive) in TrackingCapabilities.ShapeRanges)
        {
            if (!allowed.HasFlag(capability))
            {
                continue;
            }
            for (var i = start; i <= endInclusive; i++)
            {
                if (IsValid(shapes[i]))
                {
                    UnifiedTracking.Data.Shapes[i].Weight = shapes[i];
                }
            }
        }

        UnifiedTracking.MarkDataUpdated(SampleTicks);
    }

    private static void UpdateEyeState(ReadOnlySpan<float> values)
    {
        // If dilation parameters are invalid
        if (IsValid(values[MaxDilation]) &&
            IsValid(values[MinDilation]) &&
            values[MaxDilation] < values[MinDilation])
        {
            return;
        }

        // Update the unified tracking to match our data structure
        var eye = UnifiedTracking.Data.Eye;
        if (IsValid(values[LeftGazeX]))
            eye.Left.Gaze.x = values[LeftGazeX];
        if (IsValid(values[LeftGazeY]))
            eye.Left.Gaze.y = values[LeftGazeY];
        if (IsValid(values[LeftPupilDiameter]))
            eye.Left.PupilDiameter_MM = values[LeftPupilDiameter];
        if (IsValid(values[LeftOpenness]))
            eye.Left.Openness = values[LeftOpenness];

        if (IsValid(values[RightGazeX]))
            eye.Right.Gaze.x = values[RightGazeX];
        if (IsValid(values[RightGazeY]))
            eye.Right.Gaze.y = values[RightGazeY];
        if (IsValid(values[RightPupilDiameter]))
            eye.Right.PupilDiameter_MM = values[RightPupilDiameter];
        if (IsValid(values[RightOpenness]))
            eye.Right.Openness = values[RightOpenness];

        if (IsValid(values[MaxDilation]))
            eye._maxDilation = values[MaxDilation];
        if (IsValid(values[MinDilation]))
            eye._minDilation = values[MinDilation];
    }

    private static void UpdateHeadState(ReadOnlySpan<float> values)
    {
        ref var head = ref UnifiedTracking.Data.Head;
        if (IsValid(values[HeadYaw]))
            head.HeadYaw = values[HeadYaw];
        if (IsValid(values[HeadPitch]))
            head.HeadPitch = values[HeadPitch];
        if (IsValid(values[HeadRoll]))
            head.HeadRoll = values[HeadRoll];

        if (IsValid(values[HeadPosX]))
            head.HeadPosX = values[HeadPosX];
        if (IsValid(values[HeadPosY]))
            head.HeadPosY = values[HeadPosY];
        if (IsValid(values[HeadPosZ]))
            head.HeadPosZ = values[HeadPosZ];
    }
}
