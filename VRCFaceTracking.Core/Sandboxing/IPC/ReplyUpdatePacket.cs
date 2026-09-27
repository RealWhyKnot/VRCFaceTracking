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

    [StructLayout(LayoutKind.Sequential)]
    internal class UpdateDataContiguous
    {
        internal float Eye_MaxDilation;
        internal float Eye_MinDilation;

        internal float Eye_Left_GazeX;
        internal float Eye_Left_GazeY;
        internal float Eye_Left_PupilDiameter_MM;
        internal float Eye_Left_Openness;

        internal float Eye_Right_GazeX;
        internal float Eye_Right_GazeY;
        internal float Eye_Right_PupilDiameter_MM;
        internal float Eye_Right_Openness;

        internal float Head_Yaw;
        internal float Head_Pitch;
        internal float Head_Roll;

        internal float Head_PosX;
        internal float Head_PosY;
        internal float Head_PosZ;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = EXPRESSION_COUNT)]
        internal float[] Expression_Shapes;
    }

    private const int ScalarCount = 16;
    public const int PayloadSize = (ScalarCount + EXPRESSION_COUNT) * sizeof(float);

    private readonly UpdateDataContiguous _contiguousUnifiedData = new()
    {
        Expression_Shapes = new float[EXPRESSION_COUNT]
    };

    private readonly byte[]? _payload;

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
        values[0] = source.Eye._maxDilation;
        values[1] = source.Eye._minDilation;
        values[2] = source.Eye.Left.Gaze.x;
        values[3] = source.Eye.Left.Gaze.y;
        values[4] = source.Eye.Left.PupilDiameter_MM;
        values[5] = source.Eye.Left.Openness;
        values[6] = source.Eye.Right.Gaze.x;
        values[7] = source.Eye.Right.Gaze.y;
        values[8] = source.Eye.Right.PupilDiameter_MM;
        values[9] = source.Eye.Right.Openness;
        values[10] = source.Head.HeadYaw;
        values[11] = source.Head.HeadPitch;
        values[12] = source.Head.HeadRoll;
        values[13] = source.Head.HeadPosX;
        values[14] = source.Head.HeadPosY;
        values[15] = source.Head.HeadPosZ;
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
        Buffer.BlockCopy(_payload, 0, bytes, 12, _payload.Length);

        var stamps = bytes.AsSpan(12 + _payload.Length);
        BitConverter.TryWriteBytes(stamps, SampleTicks);
        BitConverter.TryWriteBytes(stamps.Slice(8), System.Diagnostics.Stopwatch.GetTimestamp());
        return bytes;
    }

    public override void Decode(in byte[] data)
    {
        if (data.Length < 12)
        {
            return;
        }

        var structSize = BitConverter.ToInt32(data, 8);
        if (structSize != Marshal.SizeOf<UpdateDataContiguous>() || data.Length < 12 + structSize)
        {
            return;
        }

        var ptr = IntPtr.Zero;
        try
        {
            ptr = Marshal.AllocHGlobal(structSize);
            Marshal.Copy(data, 12, ptr, structSize);
            Marshal.PtrToStructure<UpdateDataContiguous>(ptr, _contiguousUnifiedData);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        var stampsOffset = 12 + structSize;
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
        if (allowed.HasFlag(TrackingCapability.Eyes))
        {
            UpdateEyeState();
        }
        if (allowed.HasFlag(TrackingCapability.Head))
        {
            UpdateHeadState();
        }

        foreach (var (capability, start, endInclusive) in TrackingCapabilities.ShapeRanges)
        {
            if (!allowed.HasFlag(capability))
            {
                continue;
            }
            for (var i = start; i <= endInclusive; i++)
            {
                if (IsValid(_contiguousUnifiedData.Expression_Shapes[i]))
                {
                    UnifiedTracking.Data.Shapes[i].Weight = _contiguousUnifiedData.Expression_Shapes[i];
                }
            }
        }

        UnifiedTracking.MarkDataUpdated(SampleTicks);
    }

    private void UpdateEyeState()
    {
        // If dilation parameters are invalid
        if (IsValid(_contiguousUnifiedData.Eye_MaxDilation) &&
            IsValid(_contiguousUnifiedData.Eye_MinDilation) &&
            _contiguousUnifiedData.Eye_MaxDilation < _contiguousUnifiedData.Eye_MinDilation)
        {
            return;
        }

        // Update the unified tracking to match our data structure
        if (IsValid(_contiguousUnifiedData.Eye_Left_GazeX))
            UnifiedTracking.Data.Eye.Left.Gaze.x = _contiguousUnifiedData.Eye_Left_GazeX;
        if (IsValid(_contiguousUnifiedData.Eye_Left_GazeY))
            UnifiedTracking.Data.Eye.Left.Gaze.y = _contiguousUnifiedData.Eye_Left_GazeY;
        if (IsValid(_contiguousUnifiedData.Eye_Left_PupilDiameter_MM))
            UnifiedTracking.Data.Eye.Left.PupilDiameter_MM = _contiguousUnifiedData.Eye_Left_PupilDiameter_MM;
        if (IsValid(_contiguousUnifiedData.Eye_Left_Openness))
            UnifiedTracking.Data.Eye.Left.Openness = _contiguousUnifiedData.Eye_Left_Openness;

        if (IsValid(_contiguousUnifiedData.Eye_Right_GazeX))
            UnifiedTracking.Data.Eye.Right.Gaze.x = _contiguousUnifiedData.Eye_Right_GazeX;
        if (IsValid(_contiguousUnifiedData.Eye_Right_GazeY))
            UnifiedTracking.Data.Eye.Right.Gaze.y = _contiguousUnifiedData.Eye_Right_GazeY;
        if (IsValid(_contiguousUnifiedData.Eye_Right_PupilDiameter_MM))
            UnifiedTracking.Data.Eye.Right.PupilDiameter_MM = _contiguousUnifiedData.Eye_Right_PupilDiameter_MM;
        if (IsValid(_contiguousUnifiedData.Eye_Right_Openness))
            UnifiedTracking.Data.Eye.Right.Openness = _contiguousUnifiedData.Eye_Right_Openness;

        if (IsValid(_contiguousUnifiedData.Eye_MaxDilation))
            UnifiedTracking.Data.Eye._maxDilation = _contiguousUnifiedData.Eye_MaxDilation;
        if (IsValid(_contiguousUnifiedData.Eye_MinDilation))
            UnifiedTracking.Data.Eye._minDilation = _contiguousUnifiedData.Eye_MinDilation;
    }

    private void UpdateHeadState()
    {
        if (IsValid(_contiguousUnifiedData.Head_Yaw))
            UnifiedTracking.Data.Head.HeadYaw = _contiguousUnifiedData.Head_Yaw;
        if (IsValid(_contiguousUnifiedData.Head_Pitch))
            UnifiedTracking.Data.Head.HeadPitch = _contiguousUnifiedData.Head_Pitch;
        if (IsValid(_contiguousUnifiedData.Head_Roll))
            UnifiedTracking.Data.Head.HeadRoll = _contiguousUnifiedData.Head_Roll;

        if (IsValid(_contiguousUnifiedData.Head_PosX))
            UnifiedTracking.Data.Head.HeadPosX = _contiguousUnifiedData.Head_PosX;
        if (IsValid(_contiguousUnifiedData.Head_PosY))
            UnifiedTracking.Data.Head.HeadPosY = _contiguousUnifiedData.Head_PosY;
        if (IsValid(_contiguousUnifiedData.Head_PosZ))
            UnifiedTracking.Data.Head.HeadPosZ = _contiguousUnifiedData.Head_PosZ;
    }
}
