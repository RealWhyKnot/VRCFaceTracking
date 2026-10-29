using System.Diagnostics.CodeAnalysis;
using VRCFaceTracking.Core.Sandboxing.Ipc;

namespace VRCFaceTracking.Core.Sandboxing;

public sealed class VrcftPacketDecoder
{
    public static bool TryDecodePacket(in byte[] data, [NotNullWhen(true)] out IpcPacket? packet)
    {
        packet = null;

        if (data == null || data.Length < IpcPacket.PacketMagicSize + IpcPacket.PacketTypeSize)
        {
            return false;
        }

        for (int i = 0; i < IpcPacket.PacketMagicSize; i++)
        {
            if (data[i] != IpcPacket.HandshakeMagic[i])
            {
                return false;
            }
        }

        IpcPacket? candidate = CreatePacket((IpcPacket.PacketType)BitConverter.ToUInt32(data, IpcPacket.PacketMagicSize));
        if (candidate == null)
        {
            return false;
        }

        try
        {
            candidate.Decode(data);
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or InvalidDataException)
        {
            return false;
        }

        packet = candidate;
        return true;
    }

    private static IpcPacket? CreatePacket(IpcPacket.PacketType type) => type switch
    {
        IpcPacket.PacketType.Handshake => new HandshakePacket(),
        IpcPacket.PacketType.SplitPacketChunk => new PartialPacket(),
        IpcPacket.PacketType.EventGetSupported => new EventInitGetSupported(),
        IpcPacket.PacketType.EventInit => new EventInitPacket(),
        IpcPacket.PacketType.EventTeardown => new EventTeardownPacket(),
        IpcPacket.PacketType.EventUpdate => new EventUpdatePacket(),
        IpcPacket.PacketType.EventUpdateStatus => new EventStatusUpdatePacket(),
        IpcPacket.PacketType.EventLog => new EventLogPacket(),
        IpcPacket.PacketType.EventSetVerbose => new EventSetVerbosePacket(),
        IpcPacket.PacketType.EventSetImageStream => new EventSetImageStreamPacket(),
        IpcPacket.PacketType.ReplyGetSupported => new ReplySupportedPacket(),
        IpcPacket.PacketType.ReplyInit => new ReplyInitPacket(),
        IpcPacket.PacketType.ReplyUpdate => new ReplyUpdatePacket(),
        IpcPacket.PacketType.ReplyTeardown => new ReplyTeardownPacket(),
        IpcPacket.PacketType.DebugStreamFrame => new ImageFrameUpdatePacket(),
        IpcPacket.PacketType.Unknown => null,
        _ => null,
    };
}
