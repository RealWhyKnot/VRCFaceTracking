using VRCFaceTracking.Core.Mdns.Parsing;

namespace VRCFaceTracking.Core.Mdns;

internal class DnsQuestion
{
    public List<string> Labels { get; set; }
    public ushort Type { get; set; }
    public ushort Class { get; set; }

    public DnsQuestion(BigReader reader)
    {
        Labels = reader.ReadDomainLabels();
        Type = reader.ReadUInt16();
        Class = reader.ReadUInt16();
    }

    public DnsQuestion(List<string> labels, ushort type, ushort @class)
    {
        Labels = labels;
        Type = type;
        Class = @class;
    }

    public virtual byte[] Serialize()
    {
        List<byte> bytes =
        [
            .. BigWriter.WriteDomainLabels(Labels),
            .. BigWriter.WriteUInt16(Type),
            .. BigWriter.WriteUInt16(Class),
        ];
        return [.. bytes];
    }
}
