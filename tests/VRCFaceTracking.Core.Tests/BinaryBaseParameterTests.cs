using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Osc.DataTypes;
using VRCFaceTracking.Core.Params;

namespace VRCFaceTracking.Core.Tests;

[Collection("UnifiedTrackingState")]
public sealed class BinaryBaseParameterTests
{
    private sealed record ParamDef(string Address, string Name, Type Type) : IParameterDefinition;

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(1024, 10)]
    [InlineData(1 << 30, 30)]
    [InlineData(0, null)]
    [InlineData(-8, null)]
    [InlineData(3, null)]
    [InlineData(int.MaxValue, null)]
    public void GetBinaryStepsOnlyAcceptsPowersOfTwo(int index, int? expected) => Assert.Equal(expected, BinaryBaseParameter.GetBinarySteps(index));

    [Fact]
    public void ResetParamToleratesTheSameBitFromTwoFolders()
    {
        using var binary = new BinaryBaseParameter("v2/TestBinary", _ => 0.5f);
        IParameterDefinition[] defs =
        [
            new ParamDef("/avatar/parameters/v2/TestBinary1", "TestBinary1", typeof(bool)),
            new ParamDef("/avatar/parameters/FT/v2/TestBinary1", "TestBinary1", typeof(bool)),
            new ParamDef("/avatar/parameters/v2/TestBinary2", "TestBinary2", typeof(bool)),
        ];

        Parameter[] relevant = binary.ResetParam(defs);

        Assert.Equal(2, relevant.Length);
    }

    [Fact]
    public void ResetParamIgnoresIndexesThatAreNotBits()
    {
        using var binary = new BinaryBaseParameter("v2/TestBinary", _ => 0.5f);
        IParameterDefinition[] defs =
        [
            new ParamDef("/avatar/parameters/v2/TestBinary2147483647", "TestBinary2147483647", typeof(bool)),
            new ParamDef("/avatar/parameters/v2/TestBinary99999999999999999999", "TestBinary99999999999999999999", typeof(bool)),
            new ParamDef("/avatar/parameters/v2/TestBinary3", "TestBinary3", typeof(bool)),
        ];

        Assert.Empty(binary.ResetParam(defs));
    }
}
