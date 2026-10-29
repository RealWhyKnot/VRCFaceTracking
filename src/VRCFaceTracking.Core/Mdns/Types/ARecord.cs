using System.Net;
using VRCFaceTracking.Core.Mdns.Parsing;

namespace VRCFaceTracking.Core.Mdns.Types;

internal sealed class ARecord : IDnsSerializer
{
    public IPAddress Address { get; set; } = IPAddress.Any;

    public byte[] Serialize() => Address.GetAddressBytes();

    public void Deserialize(BigReader reader, int expectedLength)
    {
        byte[] address = reader.ReadBytes(expectedLength);
        if (address.Length != 4)
        {
            throw new InvalidDataException($"A record holds {address.Length} bytes instead of 4");
        }
        Address = new IPAddress(address);
    }
}
