using VRCFaceTracking.Core.Contracts;

namespace VRCFaceTracking.Core.Osc.Query;

public sealed class OscQueryParameterDefinition : IParameterDefinition
{
    public string Address
    {
        get;
    }
    public string Name
    {
        get;
    }
    public Type Type
    {
        get;
    }

    public OscQueryParameterDefinition(string address, OscQueryNode node)
    {
        Address = address;
        Name = node.Name;
        char typeChar = node.OscType is { Length: > 0 } oscType ? oscType[0] : throw new ArgumentException("The node has no OSC type", nameof(node));
        Type = OscUtils.TypeConversions.First(t =>
                t.Key.typeChar
                    .Contains(typeChar))
            .Key
            .Item1;
    }
}
