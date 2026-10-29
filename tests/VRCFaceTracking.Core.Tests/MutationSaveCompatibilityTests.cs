using VRCFaceTracking.Core.Helpers;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

public sealed class MutationSaveCompatibilityTests
{
    [Fact]
    public async Task OldCalibrationSaveStillLoads()
    {
        const string Saved = "{\"calibrationBlend\":0.5,\"continuousCalibration\":true,\"calData\":{\"Shapes\":[{\"name\":\"EyeSquintRight\",\"progress\":0.25,\"max\":0.8}]},\"Name\":\"Calibration\",\"Step\":0,\"IsActive\":true,\"IsSaved\":true}";

        Calibration loaded = (await JsonText.ToObjectAsync<Calibration>(Saved, CancellationToken.None))!;

        Assert.True(loaded.IsActive);
        Assert.Equal(0.5f, loaded.CalibrationBlend);
        Assert.True(loaded.ContinuousCalibration);
        CalibrationParameter shape = Assert.Single(loaded.CalData.Shapes);
        Assert.Equal("EyeSquintRight", shape.Name);
        Assert.Equal(0.25f, shape.Progress);
        Assert.Equal(0.8f, shape.Max);
    }

    [Fact]
    public async Task CalibrationSaveRoundTripsWithoutDuplicatingShapes()
    {
        var calibration = new Calibration();
        calibration.CalData.Shapes[3].Max = 0.7f;

        Calibration loaded = (await JsonText.ToObjectAsync<Calibration>(await JsonText.StringifyAsync(calibration, CancellationToken.None), CancellationToken.None))!;

        Assert.Equal((int)UnifiedExpressions.Max, loaded.CalData.Shapes.Count);
        Assert.Equal(0.7f, loaded.CalData.Shapes[3].Max);
    }

    [Fact]
    public async Task OldParameterAdjustmentSaveStillLoads()
    {
        const string Saved = "{\"jawOpen\":{\"Item1\":0.1,\"Item2\":0.9},\"headRotationYaw\":{\"Item1\":-0.5,\"Item2\":0.5},\"Name\":\"Parameter Adjustment\",\"Step\":1,\"IsActive\":true,\"IsSaved\":false}";

        ParameterAdjustment loaded = (await JsonText.ToObjectAsync<ParameterAdjustment>(Saved, CancellationToken.None))!;

        Assert.Equal((0.1f, 0.9f), loaded.JawOpen);
        Assert.Equal((-0.5f, 0.5f), loaded.HeadRotationYaw);
        Assert.Equal((0f, 1f), loaded.MouthSmile);
    }

    [Fact]
    public async Task OldMouthBrowAssistsSaveStillLoads()
    {
        const string Saved = "{\"mouthCloseCompensation\":true,\"smileOpenAssist\":true,\"smileAssistStrength\":0.8,\"idleMouthAutoClose\":false,\"idleCloseStrength\":0.5,\"browSync\":true,\"browSyncStrength\":0.3,\"Name\":\"Mouth & Brow Assists\",\"Step\":2,\"IsActive\":true,\"IsSaved\":false}";

        MouthBrowAssists loaded = (await JsonText.ToObjectAsync<MouthBrowAssists>(Saved, CancellationToken.None))!;

        Assert.True(loaded.MouthCloseCompensation);
        Assert.True(loaded.SmileOpenAssist);
        Assert.Equal(0.8f, loaded.SmileAssistStrength);
        Assert.True(loaded.BrowSync);
        Assert.Equal(0.3f, loaded.BrowSyncStrength);
    }

    [Fact]
    public void ComponentsFollowDeclarationOrder()
    {
        var mutation = new Correctors();

        mutation.CreateProperties();

        Assert.Equal(
            ["MouthClosed/JawOpen Clamp", "LipSuck Limiter", "EyeLook Symmetrize"],
            mutation.Components.Select(c => c.Name), StringComparer.Ordinal);
        Assert.Equal(["Mouth", "Mouth", "Eyes"], mutation.Components.Select(c => c.Group), StringComparer.Ordinal);
        Assert.Equal([true, false, true], mutation.Components.Select(c => c.StartsGroup));
    }

    [Fact]
    public void StaticSettingsShowAsComponents()
    {
        var mutation = new Filter();

        mutation.CreateProperties();

        Assert.Equal(
            ["Minimum Cutoff", "Beta", "Derivative Cutoff"],
            mutation.Components.Select(c => c.Name), StringComparer.Ordinal);
    }
}
