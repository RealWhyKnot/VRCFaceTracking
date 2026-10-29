using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Osc.DataTypes;
using VRCFaceTracking.Core.Params;

namespace VRCFaceTracking.Core.Tests;

public sealed class ParameterDisposalTests
{
    private sealed record ParamDef(string Address, string Name, Type Type) : IParameterDefinition;

    [Fact]
    public void BaseParamDisposeDoesNotThrow()
    {
        var param = new BaseParam<bool>("v2/Test", _ => true);
        param.Dispose();
        param.Dispose();
    }

    [Fact]
    public void BinaryBaseParameterResetParamDisposesAndClearsChildren()
    {
        using var binary = new BinaryBaseParameter("v2/TestBinary", _ => 0.5f);
        var defs = new IParameterDefinition[]
        {
            new ParamDef("/avatar/parameters/v2/TestBinary1", "v2/TestBinary1", typeof(bool)),
        };

        binary.ResetParam(defs);
        (string, Parameter)[] namesWithChild = binary.GetParamNames();

        binary.ResetParam([]);
        (string, Parameter)[] namesAfterReset = binary.GetParamNames();

        Assert.True(namesWithChild.Length > namesAfterReset.Length);
        Assert.Single(namesAfterReset);
    }
}
