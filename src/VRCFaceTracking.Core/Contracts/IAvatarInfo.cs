namespace VRCFaceTracking.Core.Contracts;

public interface IAvatarInfo
{
    string Name
    {
        get;
    }
    string Id
    {
        get;
    }
    IReadOnlyList<IParameterDefinition> Parameters
    {
        get;
    }
    bool FullFaceTracking
    {
        get;
    }
}
