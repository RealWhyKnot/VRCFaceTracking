using VRCFaceTracking.Core.Mdns.Parsing;

namespace VRCFaceTracking.Core.Mdns;

internal interface IDnsSerializer
{
    byte[] Serialize();
    void Deserialize(BigReader reader, int expectedLength);
}
