using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Sandboxing.Ipc;

public sealed class CameraControlPacket : IpcPacket
{
    public const int HeaderSize = 30;

    public CameraControlPacket()
    {
    }

    public CameraControlPacket(CameraControlCommand command)
    {
        Command = command;
    }

    public CameraControlCommand Command { get; set; } = new(string.Empty, CameraControlKind.Recenter);

    public override PacketType GetPacketType() => PacketType.EventCameraControl;

    public override byte[] GetBytes()
    {
        byte[] id = IpcText.Encode(Command.StreamId);
        byte[] packet = new byte[HeaderSize + id.Length];
        Buffer.BlockCopy(HandshakeMagic, 0, packet, 0, PacketMagicSize);
        BitConverter.TryWriteBytes(packet.AsSpan(4), (uint)GetPacketType());
        packet[8] = (byte)Command.Kind;
        BitConverter.TryWriteBytes(packet.AsSpan(9), Command.Value);
        BitConverter.TryWriteBytes(packet.AsSpan(13), Command.X);
        BitConverter.TryWriteBytes(packet.AsSpan(17), Command.Y);
        BitConverter.TryWriteBytes(packet.AsSpan(21), Command.Width);
        BitConverter.TryWriteBytes(packet.AsSpan(25), Command.Height);
        packet[29] = (byte)id.Length;
        Buffer.BlockCopy(id, 0, packet, HeaderSize, id.Length);
        return packet;
    }

    public override void Decode(in byte[] data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("Camera control packet is truncated");
        }

        var kind = (CameraControlKind)data[8];
        float value = BitConverter.ToSingle(data, 9);
        float x = BitConverter.ToSingle(data, 13);
        float y = BitConverter.ToSingle(data, 17);
        float width = BitConverter.ToSingle(data, 21);
        float height = BitConverter.ToSingle(data, 25);
        int idLength = data[29];
        if (!Enum.IsDefined(kind) || !float.IsFinite(value) || !float.IsFinite(x) || !float.IsFinite(y) ||
            !float.IsFinite(width) || !float.IsFinite(height) || data.Length < HeaderSize + idLength)
        {
            throw new InvalidDataException("Camera control packet is malformed");
        }

        Command = new CameraControlCommand(IpcText.Decode(data, HeaderSize, idLength), kind, value, x, y, width, height);
    }
}
