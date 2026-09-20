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

    private readonly UpdateDataContiguous _contiguousUnifiedData = new()
    {
        Expression_Shapes = new float[EXPRESSION_COUNT]
    };

    private const int StampCount = 3;
    public long PokeSentTicks;
    public long PokeReceivedTicks;
    public long ReplySentTicks;

    public override PacketType GetPacketType() => PacketType.ReplyUpdate;

    // We send a challenge to the vrcft host, and if we receive a reply with the same data, we consider the connection successfully ACKed.
    // In other words, this packet is the handshake begin and ACK packet.

    public override byte[] GetBytes()
    {
        // Build handshake packet

        var packetTypeBytes = BitConverter.GetBytes((uint)GetPacketType());

        var packetSize = SIZE_PACKET_MAGIC + SIZE_PACKET_TYPE;

        var source = UnifiedTracking.ModuleSnapshot ?? UnifiedTracking.Data;
        lock (UnifiedTracking.DataLock)
        {
            // Update the internal data structure to match the current state of unified tracking
            _contiguousUnifiedData.Eye_Left_GazeX = source.Eye.Left.Gaze.x;
            _contiguousUnifiedData.Eye_Left_GazeY = source.Eye.Left.Gaze.y;
            _contiguousUnifiedData.Eye_Left_PupilDiameter_MM = source.Eye.Left.PupilDiameter_MM;
            _contiguousUnifiedData.Eye_Left_Openness = source.Eye.Left.Openness;

            _contiguousUnifiedData.Eye_Right_GazeX = source.Eye.Right.Gaze.x;
            _contiguousUnifiedData.Eye_Right_GazeY = source.Eye.Right.Gaze.y;
            _contiguousUnifiedData.Eye_Right_PupilDiameter_MM = source.Eye.Right.PupilDiameter_MM;
            _contiguousUnifiedData.Eye_Right_Openness = source.Eye.Right.Openness;

            _contiguousUnifiedData.Eye_MaxDilation = source.Eye._maxDilation;
            _contiguousUnifiedData.Eye_MinDilation = source.Eye._minDilation;

            _contiguousUnifiedData.Head_Yaw = source.Head.HeadYaw;
            _contiguousUnifiedData.Head_Pitch = source.Head.HeadPitch;
            _contiguousUnifiedData.Head_Roll = source.Head.HeadRoll;

            _contiguousUnifiedData.Head_PosX = source.Head.HeadPosX;
            _contiguousUnifiedData.Head_PosY = source.Head.HeadPosY;
            _contiguousUnifiedData.Head_PosZ = source.Head.HeadPosZ;

            // Copy face tracking
            for (var i = 0; i < _contiguousUnifiedData.Expression_Shapes.Length; i++)
            {
                _contiguousUnifiedData.Expression_Shapes[i] = source.Shapes[i].Weight;
            }
        }

        // Convert _contiguousUnifiedData to bytes
        var sizeStruct = Marshal.SizeOf<UpdateDataContiguous>();
        var sizeStructBytes = BitConverter.GetBytes(sizeStruct);
        var arr = new byte[sizeStruct];

        var ptr = IntPtr.Zero;
        try
        {
            ptr = Marshal.AllocHGlobal(sizeStruct);
            Marshal.StructureToPtr(_contiguousUnifiedData, ptr, false);
            Marshal.Copy(ptr, arr, 0, sizeStruct);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        packetSize = packetSize + sizeof(int) + sizeStruct + StampCount * sizeof(long);

        // Prepare buffer
        var finalDataStream = new byte[packetSize];
        Buffer.BlockCopy(HANDSHAKE_MAGIC, 0, finalDataStream, 0, SIZE_PACKET_MAGIC);     // Magic
        Buffer.BlockCopy(packetTypeBytes, 0, finalDataStream, 4, SIZE_PACKET_TYPE);      // Packet Type
        Buffer.BlockCopy(sizeStructBytes, 0, finalDataStream, 8, sizeof(int));           // Struct.Length
        Buffer.BlockCopy(arr, 0, finalDataStream, 12, sizeStruct);            // Data

        ReplySentTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        var stamps = finalDataStream.AsSpan(12 + sizeStruct);
        BitConverter.TryWriteBytes(stamps, PokeSentTicks);
        BitConverter.TryWriteBytes(stamps.Slice(8), PokeReceivedTicks);
        BitConverter.TryWriteBytes(stamps.Slice(16), ReplySentTicks);

        return finalDataStream;
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
            PokeSentTicks = BitConverter.ToInt64(data, stampsOffset);
            PokeReceivedTicks = BitConverter.ToInt64(data, stampsOffset + 8);
            ReplySentTicks = BitConverter.ToInt64(data, stampsOffset + 16);
        }
        else
        {
            PokeSentTicks = PokeReceivedTicks = ReplySentTicks = 0;
        }
    }

    internal static bool IsValid(float value) => value != INVALID_FLOAT && float.IsFinite(value);

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

        UnifiedTracking.MarkDataUpdated();
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
