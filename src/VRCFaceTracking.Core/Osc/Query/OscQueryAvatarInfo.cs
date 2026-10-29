using VRCFaceTracking.Core.Contracts;

namespace VRCFaceTracking.Core.Osc.Query;

public sealed class OscQueryAvatarInfo : IAvatarInfo
{
    internal const string UnknownName = "Unknown";

    public string Name
    {
        get; internal set;
    } = UnknownName;

    public string Id
    {
        get;
    } = "Unknown";

    public IReadOnlyList<IParameterDefinition> Parameters
    {
        get;
    } = [];

    public bool FullFaceTracking
    {
        get;
    }

    public OscQueryAvatarInfo(OscQueryNode rootNode)
    {
        if (rootNode.Contents is not { } contents || !contents.TryGetValue("parameters", out OscQueryNode? parameters))
        {
            return;
        }

        if (contents.TryGetValue("change", out OscQueryNode? change) && change is { Value: [string id, ..] })
        {
            Id = id;
        }

        if (contents.TryGetValue("name", out OscQueryNode? name) && name is { Value: [string avatarName, ..] } && !string.IsNullOrWhiteSpace(avatarName))
        {
            Name = avatarName;
        }

        FullFaceTracking = contents.TryGetValue("avatarfullfacetracking", out OscQueryNode? fullFaceTracking)
            && fullFaceTracking is { Value: [true, ..] };

        var definitions = new List<IParameterDefinition>();
        if (parameters is { Contents: { } entries })
        {
            CollectParameters(entries, definitions);
        }

        Parameters = definitions;
    }

    private static void CollectParameters(IReadOnlyDictionary<string, OscQueryNode> entries, List<IParameterDefinition> definitions)
    {
        foreach (OscQueryNode? node in entries.Values)
        {
            if (node?.Contents is { } contents)
            {
                CollectParameters(contents, definitions);
            }
            else if (node != null && OscQueryParameterDefinition.TryCreate(node, out OscQueryParameterDefinition? definition))
            {
                definitions.Add(definition);
            }
        }
    }
}
