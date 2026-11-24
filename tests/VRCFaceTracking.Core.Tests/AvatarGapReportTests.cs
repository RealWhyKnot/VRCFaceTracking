using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

public sealed class AvatarGapReportTests
{
    private static readonly IReadOnlyList<SignalRoute> CheekRoutes = [.. SignalFillIn.Routes.Where(r => r.Feature == SignalRoute.CheekFeature)];

    private static SignalCapabilitySnapshot Caps(params UnifiedExpressions[] live)
    {
        bool[] flags = new bool[(int)UnifiedExpressions.Max];
        foreach (UnifiedExpressions shape in live)
        {
            flags[(int)shape] = true;
        }

        return new SignalCapabilitySnapshot(flags, eyeLeftLive: false, eyeRightLive: false, eyeRestLeft: 1f, eyeRestRight: 1f, sampleCount: 100, version: 1);
    }

    private static AvatarSupport Support(bool native, params UnifiedExpressions[] driven) => new(native, new HashSet<UnifiedExpressions>(driven));

    private static AvatarGapStatus StatusOf(AvatarGapReport report, UnifiedExpressions shape) => report.Entries.Single(e => e.Shape == shape).Status;

    [Fact]
    public void DrivenLiveShapeIsCovered()
    {
        var report = AvatarGapReport.Build(Caps(UnifiedExpressions.JawOpen), Support(native: false, UnifiedExpressions.JawOpen), []);

        Assert.Equal(AvatarGapStatus.Covered, StatusOf(report, UnifiedExpressions.JawOpen));
        Assert.Equal(1, report.CoveredCount);
        Assert.Equal(0, report.AbsentCount);
    }

    [Fact]
    public void UndrivenLiveShapeIsMissing()
    {
        var report = AvatarGapReport.Build(Caps(UnifiedExpressions.NoseSneerLeft), Support(native: false), []);

        Assert.Equal(AvatarGapStatus.Missing, StatusOf(report, UnifiedExpressions.NoseSneerLeft));
        Assert.Equal(1, report.MissingCount);
        Assert.Equal(1, report.AbsentCount);
        Assert.Contains("NoseSneer", report.Describe(AvatarGapStatus.Missing), StringComparison.Ordinal);
    }

    [Fact]
    public void UndrivenShapeWithAnActiveRouteIsFilled()
    {
        var report = AvatarGapReport.Build(
            Caps(UnifiedExpressions.CheekSquintLeft),
            Support(native: false, UnifiedExpressions.EyeSquintLeft),
            CheekRoutes);

        AvatarGapEntry entry = report.Entries.Single(e => e.Shape == UnifiedExpressions.CheekSquintLeft);
        Assert.Equal(AvatarGapStatus.Filled, entry.Status);
        Assert.Equal("EyeSquint", entry.FillSource);
        Assert.Equal(1, report.FilledCount);
        Assert.Equal(1, report.AbsentCount);
        Assert.Contains("via EyeSquint", report.Describe(AvatarGapStatus.Filled), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutActiveRoutesTheSameShapeIsMissing()
    {
        var report = AvatarGapReport.Build(Caps(UnifiedExpressions.CheekSquintLeft), Support(native: false, UnifiedExpressions.EyeSquintLeft), []);

        Assert.Equal(AvatarGapStatus.Missing, StatusOf(report, UnifiedExpressions.CheekSquintLeft));
    }

    [Fact]
    public void NativeFaceTrackingAvatarSkipsEveryLiveShape()
    {
        var report = AvatarGapReport.Build(Caps(UnifiedExpressions.JawOpen, UnifiedExpressions.NoseSneerLeft), Support(native: true), CheekRoutes);

        Assert.Equal(2, report.NativeSkipCount);
        Assert.Equal(0, report.AbsentCount);
        Assert.All(report.Entries, e => Assert.Equal(AvatarGapStatus.NativeSkip, e.Status));
    }

    [Fact]
    public void ShapesTheHeadsetDoesNotMeasureAreNotListed()
    {
        var report = AvatarGapReport.Build(Caps(), Support(native: false), []);

        Assert.True(report.IsEmpty);
        Assert.Equal(0, report.TrackedCount);
    }

    [Fact]
    public void LeftAndRightCountAsOneSignal()
    {
        var report = AvatarGapReport.Build(Caps(UnifiedExpressions.NoseSneerLeft, UnifiedExpressions.NoseSneerRight), Support(native: false), []);

        Assert.Equal(1, report.TrackedCount);
        Assert.Equal(1, report.MissingCount);
    }

    [Fact]
    public void SameAsComparesEntries()
    {
        var a = AvatarGapReport.Build(Caps(UnifiedExpressions.NoseSneerLeft), Support(native: false), []);
        var b = AvatarGapReport.Build(Caps(UnifiedExpressions.NoseSneerLeft), Support(native: false), []);
        var c = AvatarGapReport.Build(Caps(UnifiedExpressions.NoseSneerLeft), Support(native: false, UnifiedExpressions.NoseSneerLeft), []);

        Assert.True(a.SameAs(b));
        Assert.False(a.SameAs(c));
        Assert.False(a.SameAs(other: null));
    }

    [Fact]
    public void SupportReadsDrivenShapesFromAvatarParameters()
    {
        var support = AvatarSupport.From(new AvatarParameterSnapshot(usesNativeFaceTracking: false, ["v2/JawOpen", "v2/EyeSquintLeft"]));

        Assert.True(support.Drives(UnifiedExpressions.JawOpen));
        Assert.True(support.Drives(UnifiedExpressions.EyeSquintLeft, UnifiedExpressions.EyeSquintRight));
        Assert.False(support.Drives(UnifiedExpressions.NoseSneerLeft));
    }
}
