using VRCFaceTracking.Core.Contracts;

namespace VRCFaceTracking.Core.Osc.Query;

public sealed class OscQueryAvatarInfo : IAvatarInfo
{
    public string Name
    {
        get; internal set;
    } = "Unknown";

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

        if (contents.TryGetValue("change", out OscQueryNode? change) && change.Value is [string id, ..])
        {
            Id = id;
        }
        FullFaceTracking = contents.TryGetValue("avatarfullfacetracking", out OscQueryNode? fullFaceTracking)
            && fullFaceTracking.Value is [true, ..];

        static IEnumerable<IParameterDefinition> ConstructParameterArray(IReadOnlyDictionary<string, OscQueryNode> entries)
        {
            return entries
                .SelectMany(entry =>
                    entry.Value.Contents != null ? ConstructParameterArray(entry.Value.Contents) : !string.IsNullOrEmpty(entry.Value.OscType) ? [new OscQueryParameterDefinition(entry.Value.FullPath, entry.Value)] : Array.Empty<OscQueryParameterDefinition>()
                );
        }

        Parameters = parameters.Contents is { } entries ? [.. ConstructParameterArray(entries)] : [];
    }
}
