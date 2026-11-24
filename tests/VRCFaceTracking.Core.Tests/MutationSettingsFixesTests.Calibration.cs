using VRCFaceTracking.Core.Helpers;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

public sealed partial class MutationSettingsFixesTests
{

    [Fact]
    public void IsSavedIsGoneFromEveryMutation()
    {
        Assert.All(TrackingMutation.GetImplementingMutations(), mutation => Assert.Null(mutation.GetType().GetProperty("IsSaved")));
        Assert.DoesNotContain("IsSaved", Newtonsoft.Json.JsonConvert.SerializeObject(new Calibration()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OldMouthBrowAssistsKeysStillDeserialize()
    {
        const string Saved = "{\"mouthCloseCompensation\":true,\"smileOpenAssist\":true,\"smileAssistStrength\":0.8,\"idleMouthAutoClose\":true,\"idleCloseStrength\":0.4,\"browSync\":true,\"browSyncStrength\":0.3,\"Name\":\"Mouth & Brow Assists\",\"Step\":2,\"IsActive\":true,\"IsSaved\":false}";

        MouthBrowAssists loaded = (await JsonText.ToObjectAsync<MouthBrowAssists>(Saved, CancellationToken.None))!;

        Assert.True(loaded.IsActive);
        Assert.True(loaded.IdleMouthAutoClose);
        Assert.Equal(0.4f, loaded.IdleCloseStrength);
    }

    [Fact]
    public void InventedExpressionAssistsAreGone()
    {
        string[] properties = [.. typeof(MouthBrowAssists).GetProperties().Select(p => p.Name)];

        Assert.DoesNotContain("SmileOpenAssist", properties, StringComparer.Ordinal);
        Assert.DoesNotContain("SmileAssistStrength", properties, StringComparer.Ordinal);
        Assert.DoesNotContain("BrowSync", properties, StringComparer.Ordinal);
        Assert.DoesNotContain("BrowSyncStrength", properties, StringComparer.Ordinal);
        Assert.DoesNotContain("MouthCloseCompensation", properties, StringComparer.Ordinal);
    }

    [Fact]
    public void SmileNoLongerOpensTheJawAndClosedEyesNoLongerMoveBrows()
    {
        var assists = new MouthBrowAssists { IsActive = true, IdleMouthAutoClose = true };
        var data = new UnifiedTrackingData();
        SetShape(data, UnifiedExpressions.MouthCornerPullLeft, 1f);
        SetShape(data, UnifiedExpressions.BrowInnerUpLeft, 0.5f);
        data.Eye.Left.Openness = 0f;

        assists.MutateData(ref data);

        Assert.Equal(0f, Shape(data, UnifiedExpressions.JawOpen));
        Assert.Equal(0.5f, Shape(data, UnifiedExpressions.BrowInnerUpLeft));
        Assert.Equal(0f, Shape(data, UnifiedExpressions.BrowLowererLeft));
        Assert.Equal(0f, Shape(data, UnifiedExpressions.BrowPinchLeft));
    }

    [Theory]
    [InlineData(0.5f, 0.5f, 0.2f)]
    [InlineData(0.5f, 0.2f, 0.38f)]
    [InlineData(0.9f, 0f, 0.9f)]
    public void CorrectorsCompensationAndClampAgreeOnTheMouth(float jaw, float mouthClosed, float expectedJaw)
    {
        var correctors = new Correctors { MouthCloseCompensation = true };
        var data = new UnifiedTrackingData();
        SetShape(data, UnifiedExpressions.JawOpen, jaw);
        SetShape(data, UnifiedExpressions.MouthClosed, mouthClosed);

        correctors.MutateData(ref data);

        Assert.Equal(expectedJaw, Shape(data, UnifiedExpressions.JawOpen), 4);
        Assert.True(Shape(data, UnifiedExpressions.MouthClosed) <= Shape(data, UnifiedExpressions.JawOpen) + 0.0001f);
    }

    [Fact]
    public void CorrectorsLeaveTheJawAloneByDefault()
    {
        var correctors = new Correctors();
        var data = new UnifiedTrackingData();
        SetShape(data, UnifiedExpressions.JawOpen, 0.5f);
        SetShape(data, UnifiedExpressions.MouthClosed, 0.5f);

        correctors.MutateData(ref data);

        Assert.Equal(0.5f, Shape(data, UnifiedExpressions.JawOpen));
    }

    [Fact]
    public async Task CompensationMovesFromTheAssistsSave()
    {
        var settings = new SavedSettings(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Mouth & Brow Assists"] = "{\"mouthCloseCompensation\":true,\"Name\":\"Mouth & Brow Assists\"}",
        });
        var correctors = new Correctors();

        await correctors.MoveCompensationFromAssistsAsync(settings, CancellationToken.None);

        Assert.True(correctors.MouthCloseCompensation);
    }

    [Fact]
    public async Task MovedCompensationIsSavedUnderCorrectors()
    {
        var settings = new SavedSettings(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Mouth & Brow Assists"] = "{\"mouthCloseCompensation\":true,\"Name\":\"Mouth & Brow Assists\"}",
        });
        var correctors = new Correctors { LocalSettingsService = settings };

        await correctors.MoveCompensationFromAssistsAsync(settings, CancellationToken.None);
        settings.Remove("Mouth & Brow Assists");
        var reloaded = new Correctors();
        await reloaded.MoveCompensationFromAssistsAsync(settings, CancellationToken.None);
        Correctors? saved = await settings.ReadSettingAsync<Correctors>("Unified Correctors", defaultValue: null, forceLocal: true, CancellationToken.None);

        Assert.True(saved?.MouthCloseCompensation);
    }

    [Fact]
    public async Task CompensationMoveRespectsAnExistingCorrectorsChoice()
    {
        var settings = new SavedSettings(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Mouth & Brow Assists"] = "{\"mouthCloseCompensation\":true}",
            ["Unified Correctors"] = "{\"mouthCloseCompensation\":false}",
        });
        var correctors = new Correctors();

        await correctors.MoveCompensationFromAssistsAsync(settings, CancellationToken.None);

        Assert.False(correctors.MouthCloseCompensation);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(-0.5f)]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void DefaultHeadRangeIsAnIdentityMapping(float value)
    {
        var adjustment = new ParameterAdjustment();
        var data = new UnifiedTrackingData();
        data.Head.HeadYaw = value;
        data.Head.HeadPitch = value;
        data.Head.HeadRoll = value;
        data.Head.HeadPosX = value;
        data.Head.HeadPosY = value;
        data.Head.HeadPosZ = value;

        adjustment.MutateData(ref data);

        Assert.Equal(value, data.Head.HeadYaw, 5);
        Assert.Equal(value, data.Head.HeadPitch, 5);
        Assert.Equal(value, data.Head.HeadRoll, 5);
        Assert.Equal(value, data.Head.HeadPosX, 5);
        Assert.Equal(value, data.Head.HeadPosY, 5);
        Assert.Equal(value, data.Head.HeadPosZ, 5);
    }

    [Fact]
    public void NarrowedHeadRangeStretchesAcrossMinusOneToOne()
    {
        var adjustment = new ParameterAdjustment { HeadRotationYaw = (-0.5f, 0.5f) };
        var data = new UnifiedTrackingData();
        data.Head.HeadYaw = 0.25f;

        adjustment.MutateData(ref data);

        Assert.Equal(0.5f, data.Head.HeadYaw, 5);
    }

    [Fact]
    public void NonHeadRangesStillMapToZeroToOne()
    {
        var adjustment = new ParameterAdjustment { JawOpen = (0.2f, 0.6f) };
        var data = new UnifiedTrackingData();
        SetShape(data, UnifiedExpressions.JawOpen, 0.4f);

        adjustment.MutateData(ref data);

        Assert.Equal(0.5f, Shape(data, UnifiedExpressions.JawOpen), 5);
    }

    [Fact]
    public async Task SavedZeroToOneHeadRangesLoadAsMinusOneToOneAndAreSaved()
    {
        var settings = new SavedSettings(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Parameter Adjustment"] = "{\"headRotationYaw\":{\"Item1\":0.0,\"Item2\":1.0},\"headPositionZ\":{\"Item1\":0.0,\"Item2\":1.0},\"jawOpen\":{\"Item1\":0.0,\"Item2\":1.0}}",
        });
        ParameterAdjustment adjustment = (await settings.ReadSettingAsync<ParameterAdjustment>("Parameter Adjustment", defaultValue: null, forceLocal: true, CancellationToken.None))!;
        adjustment.LocalSettingsService = settings;
        Assert.Equal((0f, 1f), adjustment.HeadRotationYaw);

        await adjustment.MigrateHeadRangesAsync(CancellationToken.None);

        Assert.Equal((-1f, 1f), adjustment.HeadRotationYaw);
        Assert.Equal((-1f, 1f), adjustment.HeadPositionZ);
        Assert.Equal((0f, 1f), adjustment.JawOpen);
        ParameterAdjustment saved = (await settings.ReadSettingAsync<ParameterAdjustment>("Parameter Adjustment", defaultValue: null, forceLocal: true, CancellationToken.None))!;
        Assert.Equal((-1f, 1f), saved.HeadRotationYaw);
        Assert.Equal((-1f, 1f), saved.HeadPositionZ);
        Assert.Equal(ParameterAdjustment.CurrentHeadRangeVersion, saved.HeadRangeVersion);
    }

    [Fact]
    public async Task HeadRangeMigrationRunsOnlyOnce()
    {
        var settings = new SavedSettings(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Parameter Adjustment"] = "{\"headRotationYaw\":{\"Item1\":0.0,\"Item2\":1.0}}",
        });
        ParameterAdjustment adjustment = (await settings.ReadSettingAsync<ParameterAdjustment>("Parameter Adjustment", defaultValue: null, forceLocal: true, CancellationToken.None))!;
        adjustment.LocalSettingsService = settings;
        await adjustment.MigrateHeadRangesAsync(CancellationToken.None);

        adjustment.HeadRotationYaw = (0f, 1f);
        await adjustment.MigrateHeadRangesAsync(CancellationToken.None);

        Assert.Equal((0f, 1f), adjustment.HeadRotationYaw);
    }

    [Fact]
    public async Task SavedCustomHeadRangeIsLeftAlone()
    {
        var settings = new SavedSettings(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Parameter Adjustment"] = "{\"headRotationYaw\":{\"Item1\":-0.4,\"Item2\":0.7},\"headPositionX\":{\"Item1\":0.0,\"Item2\":0.5},\"jawOpen\":{\"Item1\":0.1,\"Item2\":0.9}}",
        });
        ParameterAdjustment adjustment = (await settings.ReadSettingAsync<ParameterAdjustment>("Parameter Adjustment", defaultValue: null, forceLocal: true, CancellationToken.None))!;
        adjustment.LocalSettingsService = settings;

        await adjustment.MigrateHeadRangesAsync(CancellationToken.None);

        Assert.Equal((-0.4f, 0.7f), adjustment.HeadRotationYaw);
        Assert.Equal((0f, 0.5f), adjustment.HeadPositionX);
        Assert.Equal((-1f, 1f), adjustment.HeadRotationPitch);
        Assert.Equal((0.1f, 0.9f), adjustment.JawOpen);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(32)]
    [InlineData(64)]
    public async Task SavedProgressLoadsAsTheSameFractionAtAnyWindowSize(int window)
    {
        var calibration = new Calibration { IsActive = true, WindowSize = window };
        calibration.CalData.Shapes[(int)UnifiedExpressions.JawOpen].Progress = 1f;
        calibration.CalData.Shapes[(int)UnifiedExpressions.JawOpen].Max = 0.8f;
        calibration.CalData.Shapes[(int)UnifiedExpressions.MouthClosed].Progress = 0.5f;

        Calibration loaded = (await JsonText.ToObjectAsync<Calibration>(await JsonText.StringifyAsync(calibration, CancellationToken.None), CancellationToken.None))!;
        CalibrationParameter jaw = loaded.CalData.Shapes[(int)UnifiedExpressions.JawOpen];
        CalibrationParameter closed = loaded.CalData.Shapes[(int)UnifiedExpressions.MouthClosed];

        Assert.Equal(window, loaded.WindowSize);
        Assert.Equal(1f, jaw.Progress, 4);
        Assert.Equal(0.5f, closed.Progress, 4);

        var data = new UnifiedTrackingData();
        loaded.MutateData(ref data);

        Assert.Equal(1f, jaw.Progress, 4);
        Assert.Equal(0.5f, closed.Progress, 4);
    }

    [Fact]
    public async Task SavedProgressSurvivesWindowSizeListedBeforeCalibrationData()
    {
        const string Saved = "{\"windowSize\":128,\"calData\":{\"Shapes\":[{\"name\":\"JawOpen\",\"progress\":1.0,\"max\":0.8}]},\"IsActive\":true}";

        Calibration loaded = (await JsonText.ToObjectAsync<Calibration>(Saved, CancellationToken.None))!;
        var data = new UnifiedTrackingData();
        loaded.MutateData(ref data);

        Assert.Equal(1f, loaded.CalData.Shapes[0].Progress, 4);
    }

    [Fact]
    public async Task SavedProgressSurvivesWindowSizeListedAfterCalibrationData()
    {
        const string Saved = "{\"calData\":{\"Shapes\":[{\"name\":\"JawOpen\",\"progress\":1.0,\"max\":0.8}]},\"windowSize\":128,\"IsActive\":true}";

        Calibration loaded = (await JsonText.ToObjectAsync<Calibration>(Saved, CancellationToken.None))!;
        var data = new UnifiedTrackingData();
        loaded.MutateData(ref data);

        Assert.Equal(1f, loaded.CalData.Shapes[0].Progress, 4);
    }

    [Fact]
    public async Task EyelidBlendMovedFromCorrectorsIsSavedUnderEyeAssists()
    {
        var settings = new SavedSettings(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Unified Correctors"] = "{\"eyeLidBlend\":0.4}",
        });
        var assists = new EyeAssists { LocalSettingsService = settings };

        await assists.MoveEyelidBlendFromCorrectorsAsync(settings, CancellationToken.None);
        settings.Remove("Unified Correctors");
        var reloaded = new EyeAssists();
        await reloaded.MoveEyelidBlendFromCorrectorsAsync(settings, CancellationToken.None);
        EyeAssists? saved = await settings.ReadSettingAsync<EyeAssists>("Eye Assists", defaultValue: null, forceLocal: true, CancellationToken.None);

        Assert.Equal(0.4f, assists.EyeLidBlend);
        Assert.Equal(0.4f, saved?.EyeLidBlend);
    }
}
