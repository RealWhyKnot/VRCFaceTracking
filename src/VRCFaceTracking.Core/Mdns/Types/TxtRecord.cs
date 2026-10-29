using System.Text;
using VRCFaceTracking.Core.Mdns.Parsing;

namespace VRCFaceTracking.Core.Mdns.Types;

internal sealed class TxtRecord : IDnsSerializer
{
    public List<string> Text { get; set; }

    public TxtRecord()
    {
        Text = [];
    }

    public byte[] Serialize()
    {
        List<byte> bytes = [];
        foreach (string s in Text)
        {
            bytes.Add((byte)s.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(s));
        }
        return [.. bytes];
    }

    public void Deserialize(BigReader reader, int expectedLength)
    {
        Text = [];
        while (expectedLength > 0)
        {
            string currString = reader.ReadString();
            Text.Add(currString);
            expectedLength -= Encoding.ASCII.GetByteCount(currString) + 1;
        }
    }
}
