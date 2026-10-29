using VRCFaceTracking.Core.Contracts;

namespace VRCFaceTracking.Core.Models.ParameterDefinition;

public sealed class NullAvatarDef : IAvatarInfo
{
    public NullAvatarDef(string name, string id)
    {
        Name = name;
        Id = id;
    }

    public string Name { get; }

    public string Id { get; }

    public IReadOnlyList<IParameterDefinition> Parameters
    {
        get;
    } = [];

    public bool FullFaceTracking => false;
}
