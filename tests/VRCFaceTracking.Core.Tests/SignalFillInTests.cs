using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Tests;

[Collection("UnifiedTrackingState")]
public sealed class SignalFillInTests
{
    private sealed record ParamDef(string Address, string Name, Type Type) : IParameterDefinition;

    private static readonly AvatarParameterSnapshot PlainV2Avatar = new(usesNativeFaceTracking: false, ["v2/EyeSquintLeft", "v2/EyeSquintRight", "v2/SmileFrownLeft", "v2/SmileFrownRight"]);

    private static SignalFillIn Create(AvatarParameterSnapshot? avatar) => new() { CurrentAvatar = () => avatar };

    private static float Shape(UnifiedTrackingData data, UnifiedExpressions shape) => data.Shapes[(int)shape].Weight;

    private static void SetShape(UnifiedTrackingData data, UnifiedExpressions shape, float weight) => data.Shapes[(int)shape].Weight = weight;

    private static UnifiedTrackingData CheekAndDimple(float cheek, float eyeSquint, float dimple, float cornerPull)
    {
        var data = new UnifiedTrackingData();
        SetShape(data, UnifiedExpressions.CheekSquintLeft, cheek);
        SetShape(data, UnifiedExpressions.CheekSquintRight, cheek);
        SetShape(data, UnifiedExpressions.EyeSquintLeft, eyeSquint);
        SetShape(data, UnifiedExpressions.EyeSquintRight, eyeSquint);
        SetShape(data, UnifiedExpressions.MouthDimpleLeft, dimple);
        SetShape(data, UnifiedExpressions.MouthDimpleRight, dimple);
        SetShape(data, UnifiedExpressions.MouthCornerPullLeft, cornerPull);
        SetShape(data, UnifiedExpressions.MouthCornerPullRight, cornerPull);
        return data;
    }

    [Theory]
    [InlineData(0.6f, 0.2f, 1f, 0.6f)]
    [InlineData(0.6f, 0.2f, 0.5f, 0.3f)]
    [InlineData(0.2f, 0.6f, 1f, 0.6f)]
    [InlineData(0f, 0.4f, 1f, 0.4f)]
    public void CheekSquintRaisesEyeSquintUpToTheMeasuredCheek(float cheek, float eyeSquint, float strength, float expected)
    {
        SignalFillIn fill = Create(PlainV2Avatar);
        fill.CheekStrength = strength;
        UnifiedTrackingData data = CheekAndDimple(cheek, eyeSquint, 0f, 0f);

        fill.MutateData(ref data);

        Assert.Equal(expected, Shape(data, UnifiedExpressions.EyeSquintLeft), 4);
        Assert.Equal(expected, Shape(data, UnifiedExpressions.EyeSquintRight), 4);
        Assert.Equal(cheek, Shape(data, UnifiedExpressions.CheekSquintLeft), 4);
    }

    [Theory]
    [InlineData(0.5f, 0.1f, 1f, 0.5f)]
    [InlineData(0.5f, 0.1f, 0.5f, 0.25f)]
    [InlineData(0.3f, 0.8f, 1f, 0.8f)]
    public void DimpleRaisesCornerPullUpToTheMeasuredDimple(float dimple, float cornerPull, float strength, float expected)
    {
        SignalFillIn fill = Create(PlainV2Avatar);
        fill.DimpleStrength = strength;
        UnifiedTrackingData data = CheekAndDimple(0f, 0f, dimple, cornerPull);

        fill.MutateData(ref data);

        Assert.Equal(expected, Shape(data, UnifiedExpressions.MouthCornerPullLeft), 4);
        Assert.Equal(expected, Shape(data, UnifiedExpressions.MouthCornerPullRight), 4);
    }

    [Fact]
    public void SidesStayIndependent()
    {
        SignalFillIn fill = Create(PlainV2Avatar);
        var data = new UnifiedTrackingData();
        SetShape(data, UnifiedExpressions.CheekSquintLeft, 0.7f);
        SetShape(data, UnifiedExpressions.MouthDimpleRight, 0.4f);

        fill.MutateData(ref data);

        Assert.Equal(0.35f, Shape(data, UnifiedExpressions.EyeSquintLeft), 4);
        Assert.Equal(0f, Shape(data, UnifiedExpressions.EyeSquintRight), 4);
        Assert.Equal(0f, Shape(data, UnifiedExpressions.MouthCornerPullLeft), 4);
        Assert.Equal(0.4f, Shape(data, UnifiedExpressions.MouthCornerPullRight), 4);
    }

    [Theory]
    [InlineData("v2/CheekSquint")]
    [InlineData("v2/CheekSquint4")]
    [InlineData("CheekSquintLeft")]
    [InlineData("CheeksSquint")]
    public void AvatarWithItsOwnCheekSquintKeepsEyeSquintAsMeasured(string avatarParameter)
    {
        SignalFillIn fill = Create(new AvatarParameterSnapshot(usesNativeFaceTracking: false, [avatarParameter]));
        UnifiedTrackingData data = CheekAndDimple(0.8f, 0.1f, 0.6f, 0f);

        fill.MutateData(ref data);

        Assert.Equal(0.1f, Shape(data, UnifiedExpressions.EyeSquintLeft), 4);
        Assert.Equal(0.6f, Shape(data, UnifiedExpressions.MouthCornerPullLeft), 4);
    }

    [Theory]
    [InlineData("v2/MouthDimple")]
    [InlineData("v2/MouthDimpleNegative")]
    [InlineData("MouthDimpleRight")]
    public void AvatarWithItsOwnDimpleKeepsTheSmileAsMeasured(string avatarParameter)
    {
        SignalFillIn fill = Create(new AvatarParameterSnapshot(usesNativeFaceTracking: false, [avatarParameter]));
        UnifiedTrackingData data = CheekAndDimple(0.8f, 0.1f, 0.6f, 0.2f);

        fill.MutateData(ref data);

        Assert.Equal(0.2f, Shape(data, UnifiedExpressions.MouthCornerPullRight), 4);
        Assert.Equal(0.4f, Shape(data, UnifiedExpressions.EyeSquintRight), 4);
    }

    [Fact]
    public void NativeFaceTrackingAvatarIsLeftAlone()
    {
        SignalFillIn fill = Create(new AvatarParameterSnapshot(usesNativeFaceTracking: true, parameterNames: []));
        UnifiedTrackingData data = CheekAndDimple(0.8f, 0.1f, 0.6f, 0.2f);

        fill.MutateData(ref data);

        Assert.Equal(0.1f, Shape(data, UnifiedExpressions.EyeSquintLeft), 4);
        Assert.Equal(0.2f, Shape(data, UnifiedExpressions.MouthCornerPullLeft), 4);
    }

    [Fact]
    public void NothingChangesBeforeAnAvatarLoads()
    {
        SignalFillIn fill = Create(avatar: null);
        UnifiedTrackingData data = CheekAndDimple(0.8f, 0.1f, 0.6f, 0.2f);

        fill.MutateData(ref data);

        Assert.Equal(0.1f, Shape(data, UnifiedExpressions.EyeSquintLeft), 4);
        Assert.Equal(0.2f, Shape(data, UnifiedExpressions.MouthCornerPullLeft), 4);
    }

    [Fact]
    public void SwitchedOffRoutesStayUntouched()
    {
        SignalFillIn fill = Create(PlainV2Avatar);
        fill.CheekToEyeSquint = false;
        fill.DimpleToSmile = false;
        UnifiedTrackingData data = CheekAndDimple(0.8f, 0.1f, 0.6f, 0.2f);

        fill.MutateData(ref data);

        Assert.Equal(0.1f, Shape(data, UnifiedExpressions.EyeSquintLeft), 4);
        Assert.Equal(0.2f, Shape(data, UnifiedExpressions.MouthCornerPullLeft), 4);
    }

    [Theory]
    [InlineData(false, false, 0.4f)]
    [InlineData(true, false, 0.1f)]
    [InlineData(false, true, 0.1f)]
    public void LoadedAvatarDecidesWhetherCheekSquintIsFilledIn(bool fullFaceTracking, bool avatarHasCheekSquint, float expected)
    {
        ParameterSenderService.AllParametersRelevantStatic = false;
        IParameterDefinition[] avatar =
        [
            new ParamDef("/avatar/parameters/FT/v2/EyeSquintLeft", "FT/v2/EyeSquintLeft", typeof(float)),
            .. avatarHasCheekSquint ? [new ParamDef("/avatar/parameters/FT/v2/CheekSquint2", "FT/v2/CheekSquint2", typeof(bool))] : Array.Empty<IParameterDefinition>(),
        ];

        try
        {
            AvatarParameters.Reset(avatar, fullFaceTracking, []);
            var fill = new SignalFillIn();
            UnifiedTrackingData data = CheekAndDimple(0.8f, 0.1f, 0f, 0f);

            fill.MutateData(ref data);

            Assert.Equal(expected, Shape(data, UnifiedExpressions.EyeSquintLeft), 4);
        }
        finally
        {
            AvatarParameters.Reset([], fullFaceTracking: false, []);
        }
    }
}
