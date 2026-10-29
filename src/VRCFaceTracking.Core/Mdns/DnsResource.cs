using VRCFaceTracking.Core.Mdns.Parsing;
using VRCFaceTracking.Core.Mdns.Types;

namespace VRCFaceTracking.Core.Mdns;

internal sealed class DnsResource : DnsQuestion
{
    private static readonly Dictionary<ushort, Type> TypeMap = new()
    {
        {1, typeof(ARecord)},
        {2, typeof(NSRecord)},
        {12, typeof(PtrRecord)},
        {16, typeof(TxtRecord)},
        {33, typeof(SrvRecord)},
    };

    public TimeSpan TimeToLive { get; set; }
    public IDnsSerializer? Data { get; set; }

    public DnsResource(BigReader reader) : base(reader)
    {
        TimeToLive = TimeSpan.FromSeconds(reader.ReadUInt32());

        int dataLength = reader.ReadUInt16();
        long expectedEnd = reader.BaseStream.Position + dataLength;

        if (TypeMap.TryGetValue(Type, out Type? type) && Activator.CreateInstance(type) is IDnsSerializer data)
        {
            data.Deserialize(reader, dataLength);
            Data = data;
        }
        else
        {
            reader.ReadBytes(dataLength);
        }

        if (reader.BaseStream.Position != expectedEnd)
        {
            throw new InvalidDataException("Invalid resource record");
        }
    }

    public DnsResource(IDnsSerializer data, List<string> names) : base(names, TypeMap.First(x => x.Value == data.GetType()).Key, 1)
    {
        Data = data;
        TimeToLive = TimeSpan.FromSeconds(120);
    }

    public override byte[] Serialize()
    {
        List<byte> bytes = [.. base.Serialize(), .. BigWriter.WriteUInt32((uint)TimeToLive.TotalSeconds)];

        byte[] data = Data?.Serialize() ?? [];
        bytes.AddRange(BigWriter.WriteUInt16((ushort)data.Length));
        bytes.AddRange(data);

        return [.. bytes];
    }
}
