namespace VRCFaceTracking.Core.Contracts;

public interface IParameterDefinition
{
    string Address
    {
        get;
    }
    string Name
    {
        get;
    }

    Type Type
    {
        get;
    }
}
