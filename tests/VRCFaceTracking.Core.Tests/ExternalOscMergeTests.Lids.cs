using System.Diagnostics;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Tests;

public sealed partial class ExternalOscMergeTests
{
    private static UnifiedTrackingData MergeFresh(ExternalOscInputService service)
    {
        var data = new UnifiedTrackingData();
        service.Merge(data);
        return data;
    }

    [Fact]
    public void UnifiedPerEyeLidSplitsIntoOpennessAndWiden()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/v2/EyeLidLeft", 0.875f);

        UnifiedTrackingData data = MergeFresh(service);

        Assert.Equal(1f, data.Eye.Left.Openness, 4);
        Assert.Equal(0.5f, Weight(data, UnifiedExpressions.EyeWideLeft), 4);
        Assert.Equal(1f, data.Eye.Right.Openness);
        Assert.Equal(0f, Weight(data, UnifiedExpressions.EyeWideRight));
        Assert.True(_tracker.Snapshot.EyeLeftLive);
        Assert.True(_tracker.Snapshot.IsLive(UnifiedExpressions.EyeWideLeft));
        Assert.False(_tracker.Snapshot.IsLive(UnifiedExpressions.EyeWideRight));
    }

    [Fact]
    public void UnifiedCombinedLidDrivesBothEyes()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/EyeLid", 0.6f);

        UnifiedTrackingData data = MergeFresh(service);

        Assert.Equal(0.8f, data.Eye.Left.Openness, 4);
        Assert.Equal(0.8f, data.Eye.Right.Openness, 4);
        Assert.Equal(0f, Weight(data, UnifiedExpressions.EyeWideLeft));
        Assert.Equal(0f, Weight(data, UnifiedExpressions.EyeWideRight));
    }

    [Theory]
    [InlineData(0.75f, 0f)]
    [InlineData(0.8f, 0.2f)]
    [InlineData(1f, 1f)]
    public void UnifiedWidenStartsAboveThreeQuarters(float value, float widen)
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/EyeLidRight", value);

        UnifiedTrackingData data = MergeFresh(service);

        Assert.Equal(1f, data.Eye.Right.Openness, 4);
        Assert.Equal(widen, Weight(data, UnifiedExpressions.EyeWideRight), 4);
    }

    [Fact]
    public void ExpandedLidsUseTheirOwnWidenThreshold()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/LeftEyeLidExpanded", 0.9f);
        Send(service, "/avatar/parameters/RightEyeLidExpanded", 0.4f);

        UnifiedTrackingData data = MergeFresh(service);

        Assert.Equal(1f, data.Eye.Left.Openness, 4);
        Assert.Equal(0.5f, Weight(data, UnifiedExpressions.EyeWideLeft), 4);
        Assert.Equal(0.5f, data.Eye.Right.Openness, 4);
        Assert.Equal(0f, Weight(data, UnifiedExpressions.EyeWideRight));
    }

    [Theory]
    [InlineData("/avatar/parameters/CombinedEyeLidExpanded")]
    [InlineData("/avatar/parameters/EyeLidExpanded")]
    public void CombinedExpandedLidDrivesBothEyes(string address)
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, address, 0.96f);

        UnifiedTrackingData data = MergeFresh(service);

        Assert.Equal(1f, data.Eye.Left.Openness, 4);
        Assert.Equal(1f, data.Eye.Right.Openness, 4);
        Assert.Equal(0.8f, Weight(data, UnifiedExpressions.EyeWideLeft), 4);
        Assert.Equal(0.8f, Weight(data, UnifiedExpressions.EyeWideRight), 4);
    }

    [Fact]
    public void NegativeExpandedSqueezeClosesTheEyeAndSquints()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/RightEyeLidExpandedSqueeze", -0.6f);

        UnifiedTrackingData data = MergeFresh(service);

        Assert.Equal(0f, data.Eye.Right.Openness, 4);
        Assert.Equal(0.6f, Weight(data, UnifiedExpressions.EyeSquintRight), 4);
        Assert.Equal(0f, Weight(data, UnifiedExpressions.EyeWideRight));
        Assert.Equal(1f, data.Eye.Left.Openness);
        Assert.Equal(0f, Weight(data, UnifiedExpressions.EyeSquintLeft));
    }

    [Theory]
    [InlineData("/avatar/parameters/CombinedEyeLidExpandedSqueeze")]
    [InlineData("/avatar/parameters/EyeLidExpandedSqueeze")]
    public void PositiveCombinedSqueezeOpensWithoutSquint(string address)
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, address, 0.9f);

        UnifiedTrackingData data = MergeFresh(service);

        Assert.Equal(1f, data.Eye.Left.Openness, 4);
        Assert.Equal(1f, data.Eye.Right.Openness, 4);
        Assert.Equal(0.5f, Weight(data, UnifiedExpressions.EyeWideLeft), 4);
        Assert.Equal(0.5f, Weight(data, UnifiedExpressions.EyeWideRight), 4);
        Assert.Equal(0f, Weight(data, UnifiedExpressions.EyeSquintLeft));
        Assert.Equal(0f, Weight(data, UnifiedExpressions.EyeSquintRight));
    }

    [Fact]
    public void ClosedValuesAreInvertedIntoOpenness()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/EyeClosedLeft", 0.3f);
        Send(service, "/avatar/parameters/EyeClosed", 0.9f);

        UnifiedTrackingData data = MergeFresh(service);

        Assert.Equal(0.7f, data.Eye.Left.Openness, 4);
        Assert.Equal(0.1f, data.Eye.Right.Openness, 4);
    }

    [Theory]
    [InlineData("/avatar/parameters/CombinedEyeLid")]
    [InlineData("/avatar/parameters/EyeOpen")]
    public void CombinedOpennessDrivesBothEyes(string address)
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, address, 0.4f);

        UnifiedTrackingData data = MergeFresh(service);

        Assert.Equal(0.4f, data.Eye.Left.Openness, 4);
        Assert.Equal(0.4f, data.Eye.Right.Openness, 4);
    }

    [Fact]
    public void PerEyeOpenNamesDriveTheirEye()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/EyeOpenLeft", 0.3f);
        Send(service, "/avatar/parameters/EyeOpenRight", 0.6f);

        UnifiedTrackingData data = MergeFresh(service);

        Assert.Equal(0.3f, data.Eye.Left.Openness, 4);
        Assert.Equal(0.6f, data.Eye.Right.Openness, 4);
    }

    [Fact]
    public void EyesXDrivesBothEyesSideways()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/EyesX", -0.35f);
        Send(service, "/avatar/parameters/EyesY", 0.15f);

        UnifiedTrackingData data = MergeFresh(service);

        Assert.Equal(-0.35f, data.Eye.Left.Gaze.x);
        Assert.Equal(-0.35f, data.Eye.Right.Gaze.x);
        Assert.Equal(0.15f, data.Eye.Left.Gaze.y);
        Assert.Equal(0.15f, data.Eye.Right.Gaze.y);
    }

    [Fact]
    public void DirectOpennessBeatsDerivedOpenness()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/LeftEyeLid", 0.3f);
        Send(service, "/avatar/parameters/EyeLidLeft", 0.6f);
        Send(service, "/avatar/parameters/EyeClosedRight", 0.5f);
        Send(service, "/avatar/parameters/RightEyeLidExpanded", 0.2f);

        UnifiedTrackingData data = MergeFresh(service);

        Assert.Equal(0.3f, data.Eye.Left.Openness, 4);
        Assert.Equal(0.5f, data.Eye.Right.Openness, 4);
    }

    [Fact]
    public void DirectWidenAndSquintBeatDerivedValues()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/EyeWideLeft", 0.2f);
        Send(service, "/avatar/parameters/EyeLidLeft", 1f);
        Send(service, "/avatar/parameters/EyeSquintRight", 0.1f);
        Send(service, "/avatar/parameters/RightEyeLidExpandedSqueeze", -0.8f);

        UnifiedTrackingData data = MergeFresh(service);

        Assert.Equal(0.2f, Weight(data, UnifiedExpressions.EyeWideLeft), 4);
        Assert.Equal(0.1f, Weight(data, UnifiedExpressions.EyeSquintRight), 4);
        Assert.Equal(0f, data.Eye.Right.Openness, 4);
    }

    [Fact]
    public void PerEyeValuesBeatCombinedValues()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/EyeLidLeft", 0.875f);
        Send(service, "/avatar/parameters/EyeLid", 0.3f);
        Send(service, "/avatar/parameters/CombinedEyeLidExpandedSqueeze", -0.4f);
        Send(service, "/avatar/parameters/RightEyeLidExpandedSqueeze", -0.1f);

        UnifiedTrackingData data = MergeFresh(service);

        Assert.Equal(1f, data.Eye.Left.Openness, 4);
        Assert.Equal(0.5f, Weight(data, UnifiedExpressions.EyeWideLeft), 4);
        Assert.Equal(0.4f, Weight(data, UnifiedExpressions.EyeSquintLeft), 4);
        Assert.Equal(0f, data.Eye.Right.Openness, 4);
        Assert.Equal(0.1f, Weight(data, UnifiedExpressions.EyeSquintRight), 4);
        Assert.Equal(0f, Weight(data, UnifiedExpressions.EyeWideRight), 4);
    }

    [Fact]
    public void PerEyeDirectOpennessBeatsCombinedOpenness()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/LeftEyeLid", 0.5f);
        Send(service, "/avatar/parameters/CombinedEyeLid", 0.9f);
        Send(service, "/avatar/parameters/EyeLidExpanded", 0.16f);

        UnifiedTrackingData data = MergeFresh(service);

        Assert.Equal(0.5f, data.Eye.Left.Openness, 4);
        Assert.Equal(0.9f, data.Eye.Right.Openness, 4);
    }

    [Fact]
    public void StaleDirectOpennessFallsBackToDerivedOpenness()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/LeftEyeLid", 0.3f);
        _clock.Advance(TimeSpan.FromMilliseconds(1500));
        Send(service, "/avatar/parameters/EyeLidLeft", 0.6f);

        UnifiedTrackingData data = MergeFresh(service);

        Assert.Equal(0.8f, data.Eye.Left.Openness, 4);
    }

    [Fact]
    public void ModuleEyeLeavesExternalLidsAlone()
    {
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = 0.4f;
        _tracker.Observe(data, 1);
        data.Eye.Left.Openness = 0.6f;
        _tracker.Observe(data, 1 + Stopwatch.Frequency);
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/EyeLid", 0.3f);
        var merged = new UnifiedTrackingData();
        merged.Eye.Left.Openness = 0.6f;

        service.Merge(merged);

        Assert.Equal(0.6f, merged.Eye.Left.Openness);
        Assert.Equal(0.4f, merged.Eye.Right.Openness, 4);
    }

    [Fact]
    public void ModuleEyeBlocksDerivedLidWidenOnThatSide()
    {
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = 0.4f;
        _tracker.Observe(data, 1);
        data.Eye.Left.Openness = 0.6f;
        _tracker.Observe(data, 1 + Stopwatch.Frequency);
        Assert.True(_tracker.Snapshot.ModuleEyeLeftLive);
        Assert.False(_tracker.Snapshot.ModuleEyeRightLive);
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/EyeLid", 1f);
        var merged = new UnifiedTrackingData();
        merged.Eye.Left.Openness = 0.6f;

        service.Merge(merged);

        Assert.Equal(0f, Weight(merged, UnifiedExpressions.EyeWideLeft));
        Assert.False(_tracker.Snapshot.IsLive(UnifiedExpressions.EyeWideLeft));
        Assert.Equal(1f, Weight(merged, UnifiedExpressions.EyeWideRight), 4);
        Assert.True(_tracker.Snapshot.IsLive(UnifiedExpressions.EyeWideRight));
    }

    [Fact]
    public void ExternalEyesLearnTheirRestingOpenness()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/LeftEyeLid", 0.75f);
        Send(service, "/avatar/parameters/RightEyeLid", 0.75f);
        var fill = new SignalFillIn
        {
            IsActive = true,
            EyeLidFill = true,
            CurrentAvatar = () => new AvatarParameterSnapshot(usesNativeFaceTracking: false, ["v2/EyeSquintLeft", "v2/EyeSquintRight", "v2/EyeWideLeft", "v2/EyeWideRight"]),
            Capabilities = () => _tracker.Snapshot,
        };

        UnifiedTrackingData resting = MergeFresh(service);
        fill.MutateData(ref resting);

        Assert.True(_tracker.Snapshot.EyeLeftLive);
        Assert.Equal(0.75f, _tracker.Snapshot.EyeRestLeft, 2);
        Assert.Equal(0.75f, _tracker.Snapshot.EyeRestRight, 2);
        Assert.Equal(0f, Weight(resting, UnifiedExpressions.EyeSquintLeft));
        Assert.Equal(0f, Weight(resting, UnifiedExpressions.EyeSquintRight));
        Assert.Equal(0f, Weight(resting, UnifiedExpressions.EyeWideLeft));

        Send(service, "/avatar/parameters/LeftEyeLid", 1f);
        Send(service, "/avatar/parameters/RightEyeLid", 1f);
        UnifiedTrackingData wide = MergeFresh(service);
        fill.MutateData(ref wide);

        Assert.Equal(0.5f, Weight(wide, UnifiedExpressions.EyeWideLeft), 3);
        Assert.Equal(0.5f, Weight(wide, UnifiedExpressions.EyeWideRight), 3);
        Assert.Equal(0f, Weight(wide, UnifiedExpressions.EyeSquintLeft));
    }

    [Fact]
    public void ExternalWidenIsNotDoubledByFillIn()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/LeftEyeLid", 0.75f);
        Send(service, "/avatar/parameters/RightEyeLid", 0.75f);
        MergeFresh(service);
        Send(service, "/avatar/parameters/LeftEyeLid", 1f);
        Send(service, "/avatar/parameters/LeftEyeLidExpanded", 0.84f);
        var fill = new SignalFillIn
        {
            IsActive = true,
            EyeLidFill = true,
            EyeLidStrength = 1f,
            CurrentAvatar = () => new AvatarParameterSnapshot(usesNativeFaceTracking: false, ["v2/EyeSquintLeft", "v2/EyeSquintRight", "v2/EyeWideLeft", "v2/EyeWideRight"]),
            Capabilities = () => _tracker.Snapshot,
        };

        UnifiedTrackingData data = MergeFresh(service);
        fill.MutateData(ref data);

        Assert.Equal(0.2f, Weight(data, UnifiedExpressions.EyeWideLeft), 3);
    }

    [Fact]
    public void ExternalSourceReportsWhichPartsItDrives()
    {
        ExternalOscInputService service = Create(enabled: true);
        Assert.False(service.DrivesEyes);
        Assert.False(service.DrivesFace);

        Send(service, "/avatar/parameters/LeftEyeX", 0.2f);
        MergeFresh(service);
        Assert.True(service.DrivesEyes);
        Assert.False(service.DrivesFace);

        Send(service, "/jawOpen", 0.4f);
        MergeFresh(service);
        Assert.True(service.DrivesFace);

        _clock.Advance(TimeSpan.FromSeconds(2));
        MergeFresh(service);
        Assert.False(service.DrivesEyes);
        Assert.False(service.DrivesFace);
    }
}
