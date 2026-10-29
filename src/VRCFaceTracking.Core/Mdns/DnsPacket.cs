using VRCFaceTracking.Core.Mdns.Parsing;

namespace VRCFaceTracking.Core.Mdns;

internal sealed class DnsPacket
{
    public ushort Id { get; set; }
    public bool IsResponse { get; set; }
    public int Opcode { get; set; }
    public bool IsConflict { get; set; }
    public bool IsTruncated { get; set; }
    public bool IsTentative { get; set; }
    public int ResponseCode { get; set; }
    public DnsQuestion[] Questions { get; set; } = [];
    public DnsResource[] Answers { get; set; } = [];
    public DnsResource[] Authorities { get; set; } = [];
    public DnsResource[] Additionals { get; set; } = [];


    public DnsPacket(BigReader stream)
    {
        Id = stream.ReadUInt16();

        ushort flags = stream.ReadUInt16();
        IsResponse = (flags & 0x8000) == 0x8000;
        Opcode = (flags & 0x7800) >> 11;
        IsConflict = (flags & 0x0400) == 0x0400;
        IsTruncated = (flags & 0x0200) == 0x0200;
        IsTentative = (flags & 0x0100) == 0x0100;
        ResponseCode = flags & 0x000F;

        Questions = new DnsQuestion[stream.ReadUInt16()];
        Answers = new DnsResource[stream.ReadUInt16()];
        Authorities = new DnsResource[stream.ReadUInt16()];
        Additionals = new DnsResource[stream.ReadUInt16()];

        for (int i = 0; i < Questions.Length; i++)
        {
            Questions[i] = new DnsQuestion(stream);
        }

        for (int i = 0; i < Answers.Length; i++)
        {
            Answers[i] = new DnsResource(stream);
        }

        for (int i = 0; i < Authorities.Length; i++)
        {
            Authorities[i] = new DnsResource(stream);
        }

        for (int i = 0; i < Additionals.Length; i++)
        {
            Additionals[i] = new DnsResource(stream);
        }
    }

    public DnsPacket()
    {
        IsResponse = true;
    }

    public byte[] Serialize()
    {
        var bytes = new List<byte>();

        bytes.AddRange(BigWriter.WriteUInt16(Id));

        ushort flags = 0;
        if (IsResponse)
        {
            flags |= 0x8000;
        }

        if (IsConflict)
        {
            flags |= 0x0400;
        }

        if (IsTruncated)
        {
            flags |= 0x0200;
        }

        flags |= (ushort)(Opcode << 11);
        flags |= (ushort)(ResponseCode & 0x000F);

        bytes.AddRange(BigWriter.WriteUInt16(flags));

        bytes.AddRange(BigWriter.WriteUInt16((ushort)Questions.Length));

        bytes.AddRange(BigWriter.WriteUInt16((ushort)Answers.Length));

        bytes.AddRange(BigWriter.WriteUInt16((ushort)Authorities.Length));

        bytes.AddRange(BigWriter.WriteUInt16((ushort)Additionals.Length));

        foreach (DnsQuestion question in Questions)
        {
            bytes.AddRange(question.Serialize());
        }

        foreach (DnsResource answer in Answers)
        {
            bytes.AddRange(answer.Serialize());
        }

        foreach (DnsResource authority in Authorities)
        {
            bytes.AddRange(authority.Serialize());
        }

        foreach (DnsResource additional in Additionals)
        {
            bytes.AddRange(additional.Serialize());
        }

        return [.. bytes];
    }
}
