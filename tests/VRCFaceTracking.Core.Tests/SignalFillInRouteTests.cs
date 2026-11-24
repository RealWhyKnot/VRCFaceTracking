using VRCFaceTracking.Core.Helpers;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

[Collection("UnifiedTrackingState")]
public sealed class SignalFillInRouteTests
{
    private static readonly Dictionary<string, Case> Cases = new(StringComparer.Ordinal)
    {
        ["NoseToLip"] = new(f => f.NoseMouthFill = true, UnifiedExpressions.NoseSneerLeft, UnifiedExpressions.NoseSneerRight, UnifiedExpressions.MouthUpperUpLeft, UnifiedExpressions.MouthUpperUpRight, "v2/NoseSneerLeft", "v2/MouthUpperUpLeft"),
        ["LipToNose"] = new(f => f.NoseMouthFill = true, UnifiedExpressions.MouthUpperUpLeft, UnifiedExpressions.MouthUpperUpRight, UnifiedExpressions.NoseSneerLeft, UnifiedExpressions.NoseSneerRight, "v2/MouthUpperUpLeft", "v2/NoseSneerLeft"),
        ["LowererToPinch"] = new(f => f.BrowFill = true, UnifiedExpressions.BrowLowererLeft, UnifiedExpressions.BrowLowererRight, UnifiedExpressions.BrowPinchLeft, UnifiedExpressions.BrowPinchRight, "v2/BrowLowererLeft", "v2/BrowPinchLeft"),
        ["PinchToLowerer"] = new(f => f.BrowFill = true, UnifiedExpressions.BrowPinchLeft, UnifiedExpressions.BrowPinchRight, UnifiedExpressions.BrowLowererLeft, UnifiedExpressions.BrowLowererRight, "v2/BrowPinchLeft", "v2/BrowLowererLeft"),
        ["EyeToCheek"] = new(_ => { }, UnifiedExpressions.EyeSquintLeft, UnifiedExpressions.EyeSquintRight, UnifiedExpressions.CheekSquintLeft, UnifiedExpressions.CheekSquintRight, "v2/EyeSquintLeft", "v2/CheekSquintLeft"),
    };

    public static TheoryData<string> CaseNames => [.. Cases.Keys];

    private static SignalCapabilitySnapshot Caps(bool eyeLeft = false, bool eyeRight = false, float rest = 0.9f, params UnifiedExpressions[] live)
    {
        bool[] flags = new bool[(int)UnifiedExpressions.Max];
        foreach (UnifiedExpressions shape in live)
        {
            flags[(int)shape] = true;
        }

        return new SignalCapabilitySnapshot(flags, eyeLeft, eyeRight, rest, rest, sampleCount: 100, version: 1);
    }

    private static SignalFillIn Create(AvatarParameterSnapshot? avatar, SignalCapabilitySnapshot caps, TimeProvider? clock = null)
    {
        var fill = new SignalFillIn { IsActive = true, CurrentAvatar = () => avatar, Capabilities = () => caps };
        if (clock != null)
        {
            fill.Clock = clock;
        }

        return fill;
    }

    private static AvatarParameterSnapshot Avatar(params string[] names) => new(usesNativeFaceTracking: false, names);

    private static UnifiedTrackingData WithSource(Case route, float value)
    {
        var data = new UnifiedTrackingData();
        data.Shapes[(int)route.SourceLeft].Weight = value;
        data.Shapes[(int)route.SourceRight].Weight = value;
        return data;
    }

    private static float Weight(UnifiedTrackingData data, UnifiedExpressions shape) => data.Shapes[(int)shape].Weight;

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void LiveSourceFillsUnmeasuredTargetAndStampsTrigger(string name)
    {
        Case route = Cases[name];
        var now = new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero);
        SignalFillIn fill = Create(Avatar(route.TargetParameter), Caps(live: [route.SourceLeft, route.SourceRight]), new FixedClock(now));
        route.Enable(fill);
        fill.NoseMouthStrength = fill.BrowStrength = fill.CheekStrength = 1f;
        UnifiedTrackingData data = WithSource(route, 0.6f);

        fill.MutateData(ref data);

        Assert.Equal(0.6f, Weight(data, route.TargetLeft), 4);
        Assert.Equal(0.6f, Weight(data, route.TargetRight), 4);
        Assert.Equal(now, fill.LastTriggeredUtc);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void DeadSourceChangesNothing(string name)
    {
        Case route = Cases[name];
        SignalFillIn fill = Create(Avatar(route.TargetParameter), Caps());
        route.Enable(fill);
        UnifiedTrackingData data = WithSource(route, 0.6f);

        fill.MutateData(ref data);

        Assert.Equal(0f, Weight(data, route.TargetLeft), 4);
        Assert.Null(fill.LastTriggeredUtc);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void AlreadyMeasuredTargetIsKept(string name)
    {
        Case route = Cases[name];
        SignalFillIn fill = Create(Avatar(route.TargetParameter), Caps(live: [route.SourceLeft, route.SourceRight, route.TargetLeft]));
        route.Enable(fill);
        UnifiedTrackingData data = WithSource(route, 0.6f);

        fill.MutateData(ref data);

        Assert.Equal(0f, Weight(data, route.TargetLeft), 4);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void AvatarThatDrivesTheSourceIsLeftAlone(string name)
    {
        Case route = Cases[name];
        SignalFillIn fill = Create(Avatar(route.TargetParameter, route.SourceParameter), Caps(live: [route.SourceLeft, route.SourceRight]));
        route.Enable(fill);
        UnifiedTrackingData data = WithSource(route, 0.6f);

        fill.MutateData(ref data);

        Assert.Equal(0f, Weight(data, route.TargetLeft), 4);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void AvatarWithoutTheTargetGetsNothing(string name)
    {
        Case route = Cases[name];
        SignalFillIn fill = Create(Avatar("v2/JawOpen"), Caps(live: [route.SourceLeft, route.SourceRight]));
        route.Enable(fill);
        UnifiedTrackingData data = WithSource(route, 0.6f);

        fill.MutateData(ref data);

        Assert.Equal(0f, Weight(data, route.TargetLeft), 4);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void NativeFaceTrackingPausesTheRoute(string name)
    {
        Case route = Cases[name];
        var native = new AvatarParameterSnapshot(usesNativeFaceTracking: true, [route.TargetParameter]);
        SignalFillIn fill = Create(native, Caps(live: [route.SourceLeft, route.SourceRight]));
        route.Enable(fill);
        UnifiedTrackingData data = WithSource(route, 0.6f);

        fill.MutateData(ref data);

        Assert.Equal(0f, Weight(data, route.TargetLeft), 4);
        Assert.Equal(FillInState.NativeFaceTracking, fill.FillState);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void RealTargetValueWinsOverRoutedOne(string name)
    {
        Case route = Cases[name];
        SignalFillIn fill = Create(Avatar(route.TargetParameter), Caps(live: [route.SourceLeft, route.SourceRight]));
        route.Enable(fill);
        UnifiedTrackingData data = WithSource(route, 0.3f);
        data.Shapes[(int)route.TargetLeft].Weight = 0.9f;

        fill.MutateData(ref data);

        Assert.Equal(0.9f, Weight(data, route.TargetLeft), 4);
    }

    [Fact]
    public void NewRoutesAreOffByDefault()
    {
        var fill = new SignalFillIn();

        Assert.False(fill.NoseMouthFill);
        Assert.False(fill.BrowFill);
        Assert.False(fill.EyeLidFill);
        Assert.True(fill.CheekToEyeSquint);
        Assert.True(fill.DimpleToSmile);
    }

    [Fact]
    public void EyeLidSquintAndWideAreDerivedFromLiveEyes()
    {
        SignalFillIn fill = Create(Avatar("v2/EyeSquintLeft", "v2/EyeWideLeft"), Caps(eyeLeft: true, eyeRight: true, rest: 0.9f));
        fill.EyeLidFill = true;
        fill.EyeLidStrength = 1f;
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = 0.5f;
        data.Eye.Right.Openness = 1f;

        fill.MutateData(ref data);

        Assert.Equal(0.8f, Weight(data, UnifiedExpressions.EyeSquintLeft), 3);
        Assert.Equal(0f, Weight(data, UnifiedExpressions.EyeSquintRight), 3);
        Assert.Equal(0f, Weight(data, UnifiedExpressions.EyeWideLeft), 3);
        Assert.Equal(1f, Weight(data, UnifiedExpressions.EyeWideRight), 3);
        Assert.NotNull(fill.LastTriggeredUtc);
    }

    [Fact]
    public void EyeLidRouteNeverTouchesBrowsOrMouth()
    {
        SignalFillIn fill = Create(Avatar("v2/EyeSquintLeft", "v2/EyeWideLeft", "v2/BrowLowererLeft", "v2/MouthUpperUpLeft"), Caps(eyeLeft: true, eyeRight: true));
        fill.EyeLidFill = true;
        fill.EyeLidStrength = 1f;
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = 0.4f;
        data.Eye.Right.Openness = 1f;

        fill.MutateData(ref data);

        foreach (UnifiedExpressions shape in new[] { UnifiedExpressions.BrowLowererLeft, UnifiedExpressions.BrowInnerUpLeft, UnifiedExpressions.BrowOuterUpLeft, UnifiedExpressions.MouthUpperUpLeft, UnifiedExpressions.MouthCornerPullLeft, UnifiedExpressions.JawOpen })
        {
            Assert.Equal(0f, Weight(data, shape), 4);
        }
    }

    [Fact]
    public void EyeLidRouteNeedsLiveEyesAndUnmeasuredSquint()
    {
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = 0.5f;

        SignalFillIn dead = Create(Avatar("v2/EyeSquintLeft"), Caps());
        dead.EyeLidFill = true;
        dead.MutateData(ref data);
        Assert.Equal(0f, Weight(data, UnifiedExpressions.EyeSquintLeft), 4);

        SignalFillIn measured = Create(Avatar("v2/EyeSquintLeft"), Caps(eyeLeft: true, live: [UnifiedExpressions.EyeSquintLeft]));
        measured.EyeLidFill = true;
        measured.MutateData(ref data);
        Assert.Equal(0f, Weight(data, UnifiedExpressions.EyeSquintLeft), 4);

        var native = new AvatarParameterSnapshot(usesNativeFaceTracking: true, ["v2/EyeSquintLeft"]);
        SignalFillIn paused = Create(native, Caps(eyeLeft: true));
        paused.EyeLidFill = true;
        paused.MutateData(ref data);
        Assert.Equal(0f, Weight(data, UnifiedExpressions.EyeSquintLeft), 4);
    }

    [Fact]
    public void ClosedEyeDoesNotCountAsSquint()
    {
        Assert.Equal(0f, SignalFillIn.SquintShape(0.02f, 0.9f), 4);
        Assert.True(SignalFillIn.SquintShape(0.5f, 0.9f) > 0.5f);
        Assert.Equal(0f, SignalFillIn.WideShape(0.9f, 0.9f), 4);
    }

    [Fact]
    public void ReverseCheekRouteNeedsAMeasuredEyeSquint()
    {
        Case route = Cases["EyeToCheek"];
        SignalFillIn fill = Create(Avatar(route.TargetParameter), Caps(live: [UnifiedExpressions.EyeSquintLeft, UnifiedExpressions.EyeSquintRight]));
        fill.CheekStrength = 1f;
        UnifiedTrackingData data = WithSource(route, 0.4f);

        fill.MutateData(ref data);

        Assert.Equal(0.4f, Weight(data, UnifiedExpressions.CheekSquintLeft), 4);
        Assert.Equal(0.4f, Weight(data, UnifiedExpressions.EyeSquintLeft), 4);
    }

    [Fact]
    public async Task SavedFillInFromBeforeTheRenameStillLoads()
    {
        const string Saved = "{\"CheekToEyeSquint\":false,\"CheekStrength\":0.7,\"DimpleToSmile\":true,\"DimpleStrength\":0.25,\"Name\":\"Cheek & Dimple Fill-In\",\"Step\":2,\"IsActive\":true,\"IsSaved\":false}";

        SignalFillIn loaded = (await JsonText.ToObjectAsync<SignalFillIn>(Saved, CancellationToken.None))!;

        Assert.Equal("Cheek & Dimple Fill-In", loaded.Name);
        Assert.True(loaded.IsActive);
        Assert.False(loaded.CheekToEyeSquint);
        Assert.Equal(0.7f, loaded.CheekStrength);
        Assert.True(loaded.DimpleToSmile);
        Assert.Equal(0.25f, loaded.DimpleStrength);
        Assert.False(loaded.NoseMouthFill);
        Assert.False(loaded.BrowFill);
        Assert.False(loaded.EyeLidFill);
    }

    [Fact]
    public async Task SavedFillInRoundTripsTheNewToggles()
    {
        var fill = new SignalFillIn { NoseMouthFill = true, BrowStrength = 0.2f, EyeLidFill = true };

        SignalFillIn loaded = (await JsonText.ToObjectAsync<SignalFillIn>(await JsonText.StringifyAsync(fill, CancellationToken.None), CancellationToken.None))!;

        Assert.True(loaded.NoseMouthFill);
        Assert.Equal(0.2f, loaded.BrowStrength);
        Assert.True(loaded.EyeLidFill);
        Assert.Equal(0.5f, loaded.NoseMouthStrength);
    }

    [Fact]
    public void OnlyOneMutationUsesTheFillInCard()
    {
        Type[] fills = [.. typeof(SignalFillIn).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(TrackingMutation)) && t.Name.Contains("Fill", StringComparison.Ordinal))];

        Assert.Equal([typeof(SignalFillIn)], fills);
    }

    private sealed record Case(Action<SignalFillIn> Enable, UnifiedExpressions SourceLeft, UnifiedExpressions SourceRight, UnifiedExpressions TargetLeft, UnifiedExpressions TargetRight, string SourceParameter, string TargetParameter);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
