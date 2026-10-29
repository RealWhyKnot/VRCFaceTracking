namespace VRCFaceTracking.Core.Mdns.Parsing;

internal sealed class BigReader : BinaryReader
{
    private readonly Dictionary<int, List<string>> _nameCache = [];

    public BigReader(byte[] data) : base(new MemoryStream(data)) { }

    public override ushort ReadUInt16() => (ushort)((ReadByte() << 8) | ReadByte());

    public override uint ReadUInt32() => (uint)((ReadByte() << 24) | (ReadByte() << 16) | (ReadByte() << 8) | ReadByte());

    public override string ReadString()
    {
        byte length = ReadByte();
        byte[] data = ReadBytes(length);
        return System.Text.Encoding.ASCII.GetString(data);
    }

    public List<string> ReadDomainLabels()
    {
        int pointer = (int)BaseStream.Position;
        byte length = ReadByte();
        if ((length & 0xC0) == 0xC0)
        {
            int ptr = ((length ^ 0xC0) << 8) | ReadByte();
            if (!_nameCache.TryGetValue(ptr, out List<string>? cname))
            {
                throw new InvalidDataException($"DNS name pointer {ptr} points at no earlier name");
            }
            _nameCache[pointer] = cname;
            return cname;
        }

        var labels = new List<string>();
        if (length == 0)
        {
            return labels;
        }

        byte[] data = ReadBytes(length);
        if (data.Length != length)
        {
            throw new EndOfStreamException();
        }
        labels.Add(System.Text.Encoding.UTF8.GetString(data));
        labels.AddRange(ReadDomainLabels());
        _nameCache[pointer] = labels;

        return labels;
    }
}
