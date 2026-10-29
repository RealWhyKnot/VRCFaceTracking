using System.Text;

namespace VRCFaceTracking.Core.Mdns.Parsing;

internal sealed class BigReader : BinaryReader
{
    private const int MaxNameLabels = 127;

    private readonly Dictionary<int, List<string>> _nameCache = [];

    public BigReader(byte[] data) : base(new MemoryStream(data)) { }

    public override ushort ReadUInt16() => (ushort)((ReadByte() << 8) | ReadByte());

    public override uint ReadUInt32() => (uint)((ReadByte() << 24) | (ReadByte() << 16) | (ReadByte() << 8) | ReadByte());

    public override string ReadString()
    {
        byte length = ReadByte();
        byte[] data = ReadBytes(length);
        return Encoding.ASCII.GetString(data);
    }

    public List<string> ReadDomainLabels()
    {
        var labels = new List<string>();
        var starts = new List<int>();
        while (true)
        {
            int position = (int)BaseStream.Position;
            byte length = ReadByte();
            if ((length & 0xC0) == 0xC0)
            {
                int pointer = ((length ^ 0xC0) << 8) | ReadByte();
                if (!_nameCache.TryGetValue(pointer, out List<string>? suffix))
                {
                    throw new InvalidDataException($"DNS name pointer {pointer} points at no earlier name");
                }
                starts.Add(position);
                labels.AddRange(suffix);
                break;
            }

            if (length == 0)
            {
                break;
            }

            if (labels.Count == MaxNameLabels)
            {
                throw new InvalidDataException($"DNS name has more than {MaxNameLabels} labels");
            }

            byte[] data = ReadBytes(length);
            if (data.Length != length)
            {
                throw new EndOfStreamException();
            }
            starts.Add(position);
            labels.Add(Encoding.UTF8.GetString(data));
        }

        if (labels.Count > MaxNameLabels)
        {
            throw new InvalidDataException($"DNS name has more than {MaxNameLabels} labels");
        }

        for (int i = 0; i < starts.Count; i++)
        {
            _nameCache[starts[i]] = labels.GetRange(i, labels.Count - i);
        }

        return labels;
    }
}
