using VRCFaceTracking.Core.Mdns.Parsing;

namespace VRCFaceTracking.Core.Mdns.Types;

internal sealed class PtrRecord : IDnsSerializer
{
    public List<string> DomainLabels { get; set; }

    public PtrRecord()
    {
        DomainLabels = [];
    }

    public byte[] Serialize() => BigWriter.WriteDomainLabels(DomainLabels);

    public void Deserialize(BigReader reader, int expectedLength) => DomainLabels = reader.ReadDomainLabels();
}
