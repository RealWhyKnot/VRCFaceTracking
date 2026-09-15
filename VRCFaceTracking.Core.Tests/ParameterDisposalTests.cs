using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.OSC;
using VRCFaceTracking.Core.OSC.DataTypes;
using VRCFaceTracking.Core.Params.Data;

namespace VRCFaceTracking.Core.Tests;

public class ParameterDisposalTests
{
    private sealed record ParamDef(string Address, string Name, Type Type) : IParameterDefinition;

    [Fact]
    public void OscMessage_Dispose_IsIdempotent()
    {
        var message = new OscMessage("/avatar/parameters/Test", typeof(float));
        message.Dispose();
        message.Dispose();
    }

    [Fact]
    public void OscMessage_EncodeAfterDispose_ReturnsZero()
    {
        var message = new OscMessage("/avatar/parameters/Test", typeof(float));
        message.Dispose();
        Assert.Equal(0, message.Encode(new byte[4096]));
    }

    [Fact]
    public void BaseParam_Dispose_DoesNotThrow()
    {
        var param = new BaseParam<bool>("v2/Test", _ => true);
        param.Dispose();
    }

    [Fact]
    public void BinaryBaseParameter_ResetParam_DisposesAndClearsChildren()
    {
        var binary = new BinaryBaseParameter("v2/TestBinary", _ => 0.5f);
        var defs = new IParameterDefinition[]
        {
            new ParamDef("/avatar/parameters/v2/TestBinary1", "v2/TestBinary1", typeof(bool))
        };

        binary.ResetParam(defs);
        var namesWithChild = binary.GetParamNames();

        binary.ResetParam(Array.Empty<IParameterDefinition>());
        var namesAfterReset = binary.GetParamNames();

        Assert.True(namesWithChild.Length > namesAfterReset.Length);
        Assert.Single(namesAfterReset);
    }
}
