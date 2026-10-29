using VRCFaceTracking.Core.Mdns.Parsing;

namespace VRCFaceTracking.Core.Mdns.Types;

internal sealed class SrvRecord : IDnsSerializer
{
    public ushort Priority { get; set; }
    public ushort Weight { get; set; }
    public ushort Port { get; set; }
    public List<string> Target { get; set; } = [];

    public byte[] Serialize()
    {
        List<byte> bytes =
        [
            .. BigWriter.WriteUInt16(Priority),
            .. BigWriter.WriteUInt16(Weight),
            .. BigWriter.WriteUInt16(Port),
            .. BigWriter.WriteDomainLabels(Target),
        ];

        return [.. bytes];
    }

    public void Deserialize(BigReader reader, int expectedLength)
    {
        Priority = reader.ReadUInt16();
        Weight = reader.ReadUInt16();
        Port = reader.ReadUInt16();
        Target = reader.ReadDomainLabels();
    }
}
