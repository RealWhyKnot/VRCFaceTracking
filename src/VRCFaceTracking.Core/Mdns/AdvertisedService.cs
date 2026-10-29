using System.Net;

namespace VRCFaceTracking.Core.Mdns;

public sealed record AdvertisedService(string ServiceName, int Port, IPAddress Address)
{
    public string ServiceName { get; } = ServiceName;
    public int Port { get; } = Port;
    public IPAddress Address { get; } = Address;
}
