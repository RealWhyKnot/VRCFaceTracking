using VRCFaceTracking.Core.Contracts;

namespace VRCFaceTracking.Core.Params;

public abstract class Parameter : IDisposable
{
    public abstract Parameter[] ResetParam(IReadOnlyList<IParameterDefinition> newParams);
    public abstract (string paramName, Parameter paramLiteral)[] GetParamNames();

    public virtual bool Deprecated => false;

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
