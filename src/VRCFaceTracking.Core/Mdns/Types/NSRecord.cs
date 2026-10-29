using VRCFaceTracking.Core.Mdns.Parsing;

namespace VRCFaceTracking.Core.Mdns.Types;

internal sealed class NSRecord : IDnsSerializer
{
    public List<string> Authority { get; set; } = [];

    public byte[] Serialize() => throw new NotSupportedException();

    public void Deserialize(BigReader reader, int expectedLength) => Authority = reader.ReadDomainLabels();
}
