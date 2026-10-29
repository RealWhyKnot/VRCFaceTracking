using System.Diagnostics.CodeAnalysis;
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
        Type = node.OscType is { Length: > 0 } oscType && TryGetType(oscType[0], out Type? type)
            ? type
            : throw new ArgumentException("The node has no known OSC type", nameof(node));
    }

    private OscQueryParameterDefinition(string address, string name, Type type)
    {
        Address = address;
        Name = name;
        Type = type;
    }

    internal static bool TryCreate(OscQueryNode node, [NotNullWhen(true)] out OscQueryParameterDefinition? definition)
    {
        definition = null;
        if (node.FullPath.Length == 0 || node.OscType is not { Length: > 0 } oscType || !TryGetType(oscType[0], out Type? type))
        {
            return false;
        }

        definition = new OscQueryParameterDefinition(node.FullPath, node.Name, type);
        return true;
    }

    private static bool TryGetType(char typeChar, [NotNullWhen(true)] out Type? type)
    {
        type = OscUtils.TypeConversions.Keys.FirstOrDefault(key => key.typeChar.Contains(typeChar)).Item1;
        return type != null;
    }
}
