using Newtonsoft.Json;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

[MutationCard(TrackingCard.FillIns)]
public sealed class SignalFillIn : TrackingMutation
{
    private const float SquintStart = 0.2f;
    private const float SquintFull = 0.45f;
    private const float ClosedBelow = 0.1f;
    private const float ClosedFullyBelow = 0.25f;
    private const float WideMargin = 0.05f;

    private AvatarPlan? _plan;

    public static IReadOnlyList<SignalRoute> Routes { get; } = BuildRoutes();

    public override string Name => "Cheek & Dimple Fill-In";
    public override string Description => "Shows signals your headset measures through matching avatar shapes.";
    public override MutationPriority Priority => MutationPriority.Postprocessor;
    public override int Order => 5;
    public override int PageOrder => 2;
    public override bool IsActive { get; set; }

    [MutationProperty("Cheek Squint to Eye Squint", true, Group = "Cheeks", Order = 10)]
    public bool CheekToEyeSquint { get; set; } = true;
    [MutationProperty("Cheek Squint Strength", true, Group = "Cheeks", Indented = true, Order = 11, Advanced = true, DependsOn = nameof(CheekToEyeSquint), Step = 0.05f)]
    public float CheekStrength { get; set; } = 0.5f;

    [MutationProperty("Dimple to Smile", true, Group = "Mouth", Order = 20)]
    public bool DimpleToSmile { get; set; } = true;
    [MutationProperty("Dimple Strength", true, Group = "Mouth", Indented = true, Order = 21, Advanced = true, DependsOn = nameof(DimpleToSmile), Step = 0.05f)]
    public float DimpleStrength { get; set; } = 1f;

    [MutationProperty("Nose Sneer and Upper Lip", true, Group = "Nose", Order = 30)]
    public bool NoseMouthFill { get; set; }
    [MutationProperty("Nose Sneer and Upper Lip Strength", true, Group = "Nose", Indented = true, Order = 31, Advanced = true, DependsOn = nameof(NoseMouthFill), Step = 0.05f)]
    public float NoseMouthStrength { get; set; } = 0.5f;

    [MutationProperty("Brow Lowerer and Pinch", true, Group = "Brows", Order = 40)]
    public bool BrowFill { get; set; }
    [MutationProperty("Brow Lowerer and Pinch Strength", true, Group = "Brows", Indented = true, Order = 41, Advanced = true, DependsOn = nameof(BrowFill), Step = 0.05f)]
    public float BrowStrength { get; set; } = 0.5f;

    [MutationProperty("Squint and Wide from Eyelids", true, Group = "Eyes", Order = 50)]
    public bool EyeLidFill { get; set; }
    [MutationProperty("Squint and Wide from Eyelids Strength", true, Group = "Eyes", Indented = true, Order = 51, Advanced = true, DependsOn = nameof(EyeLidFill), Step = 0.05f)]
    public float EyeLidStrength { get; set; } = 0.5f;

    internal Func<AvatarParameterSnapshot?> CurrentAvatar { get; set; } = () => AvatarParameters.Current;

    internal Func<SignalCapabilitySnapshot> Capabilities { get; set; } = () => SignalCapabilityTracker.Shared.Snapshot;

    [JsonIgnore]
    public IReadOnlyList<SignalRoute> ActiveRoutes
    {
        get
        {
            if (!IsActive)
            {
                return [];
            }

            List<SignalRoute> active = [];
            foreach (SignalRoute route in Routes)
            {
                if (Enabled(route.Feature))
                {
                    active.Add(route);
                }
            }

            return active;
        }
    }

    [JsonIgnore]
    public FillInState FillState
    {
        get
        {
            if (!IsActive || !AnyEnabled())
            {
                return FillInState.Off;
            }

            AvatarParameterSnapshot? avatar = CurrentAvatar();
            if (avatar == null)
            {
                return FillInState.WaitingForAvatar;
            }

            if (avatar.UsesNativeFaceTracking)
            {
                return FillInState.NativeFaceTracking;
            }

            AvatarPlan plan = PlanFor(avatar);
            SignalCapabilitySnapshot capabilities = Capabilities();
            for (int i = 0; i < Routes.Count; i++)
            {
                if (Enabled(Routes[i].Feature) && Eligible(i, plan, capabilities))
                {
                    return FillInState.Filling;
                }
            }

            return FillInState.NotNeeded;
        }
    }

    [JsonIgnore]
    public override string StatusSummary => FillState switch
    {
        FillInState.WaitingForAvatar => "Waiting for avatar",
        FillInState.NativeFaceTracking => "Skipped: VRChat native face tracking active",
        FillInState.NotNeeded => "Skipped: avatar already has Cheek & Dimple parameters",
        FillInState.Filling => "Active",
        FillInState.Off => "Off",
        _ => "Off",
    };

    protected override int FeatureOf(string propertyName) => propertyName switch
    {
        nameof(CheekToEyeSquint) => SignalRoute.CheekFeature,
        nameof(DimpleToSmile) => SignalRoute.DimpleFeature,
        nameof(NoseMouthFill) => SignalRoute.NoseMouthFeature,
        nameof(BrowFill) => SignalRoute.BrowFeature,
        nameof(EyeLidFill) => SignalRoute.EyeLidFeature,
        _ => -1,
    };

    public override void MutateData(ref UnifiedTrackingData data)
    {
        AvatarParameterSnapshot? avatar = CurrentAvatar();
        if (avatar == null || avatar.UsesNativeFaceTracking)
        {
            return;
        }

        AvatarPlan plan = PlanFor(avatar);
        SignalCapabilitySnapshot capabilities = Capabilities();
        IReadOnlyList<SignalRoute> routes = Routes;
        for (int i = 0; i < routes.Count; i++)
        {
            SignalRoute route = routes[i];
            if (!Enabled(route.Feature) || !Eligible(i, plan, capabilities))
            {
                continue;
            }

            float strength = StrengthOf(route.Feature);
            switch (route.Kind)
            {
                case SignalRouteKind.Copy:
                    Copy(data, capabilities, route, route.SourceLeft!.Value, route.TargetLeft, strength);
                    Copy(data, capabilities, route, route.SourceRight!.Value, route.TargetRight, strength);
                    break;
                case SignalRouteKind.EyeSquintFromLid:
                    FromLid(data, route.TargetLeft, data.Eye.Left.Openness, capabilities.EyeRestLeft, capabilities.EyeLeftLive, wide: false, strength, route.Feature);
                    FromLid(data, route.TargetRight, data.Eye.Right.Openness, capabilities.EyeRestRight, capabilities.EyeRightLive, wide: false, strength, route.Feature);
                    break;
                case SignalRouteKind.EyeWideFromLid:
                    FromLid(data, route.TargetLeft, data.Eye.Left.Openness, capabilities.EyeRestLeft, capabilities.EyeLeftLive, wide: true, strength, route.Feature);
                    FromLid(data, route.TargetRight, data.Eye.Right.Openness, capabilities.EyeRestRight, capabilities.EyeRightLive, wide: true, strength, route.Feature);
                    break;
                default:
                    break;
            }
        }
    }

    internal static float SquintShape(float openness, float rest)
    {
        float closing = Ramp(rest - openness, SquintStart, SquintFull);
        float notClosed = Ramp(openness, ClosedBelow, ClosedFullyBelow);
        return Math.Min(closing, notClosed);
    }

    internal static float WideShape(float openness, float rest)
    {
        float start = rest + WideMargin;
        return start >= 1f ? 0f : Math.Clamp((openness - start) / (1f - start), 0f, 1f);
    }

    private static bool Eligible(int index, AvatarPlan plan, SignalCapabilitySnapshot capabilities)
    {
        SignalRoute route = Routes[index];
        if (!plan.LacksSource[index])
        {
            return false;
        }

        if (!route.Strict)
        {
            return true;
        }

        bool targetReady = plan.DrivesTarget[index] && !capabilities.IsLive(route.TargetLeft, route.TargetRight);
        bool sourceReady = route.Kind == SignalRouteKind.Copy
            ? capabilities.IsLive(route.SourceLeft!.Value, route.SourceRight!.Value)
            : capabilities.EyeLive;
        return targetReady && sourceReady;
    }

    private static float Ramp(float value, float from, float to) => Math.Clamp((value - from) / (to - from), 0f, 1f);

    private static SignalRoute[] BuildRoutes()
    {
        string[] cheek = [.. SignalShapes.NamesOf(UnifiedExpressions.CheekSquintLeft, UnifiedExpressions.CheekSquintRight), "CheekSquintLeft", "CheekSquintRight"];
        string[] eyeSquint = SignalShapes.NamesOf(UnifiedExpressions.EyeSquintLeft, UnifiedExpressions.EyeSquintRight);
        string[] eyeWide = SignalShapes.NamesOf(UnifiedExpressions.EyeWideLeft, UnifiedExpressions.EyeWideRight);
        string[] dimple = SignalShapes.NamesOf(UnifiedExpressions.MouthDimpleLeft, UnifiedExpressions.MouthDimpleRight);
        string[] cornerPull = SignalShapes.NamesOf(UnifiedExpressions.MouthCornerPullLeft, UnifiedExpressions.MouthCornerPullRight);
        string[] nose = SignalShapes.NamesOf(UnifiedExpressions.NoseSneerLeft, UnifiedExpressions.NoseSneerRight);
        string[] upperLip = [.. SignalShapes.NamesOf(UnifiedExpressions.MouthUpperUpLeft, UnifiedExpressions.MouthUpperUpRight).Where(name => !name.EndsWith("MouthOpen", StringComparison.Ordinal))];
        string[] lowerer = SignalShapes.NamesOf(UnifiedExpressions.BrowLowererLeft, UnifiedExpressions.BrowLowererRight);
        string[] pinch = SignalShapes.NamesOf(UnifiedExpressions.BrowPinchLeft, UnifiedExpressions.BrowPinchRight);

        return
        [
            Pair("CheekToEyeSquint", UnifiedExpressions.CheekSquintLeft, UnifiedExpressions.CheekSquintRight, UnifiedExpressions.EyeSquintLeft, UnifiedExpressions.EyeSquintRight, cheek, eyeSquint, SignalRoute.CheekFeature, strict: false),
            Pair("EyeToCheekSquint", UnifiedExpressions.EyeSquintLeft, UnifiedExpressions.EyeSquintRight, UnifiedExpressions.CheekSquintLeft, UnifiedExpressions.CheekSquintRight, eyeSquint, cheek, SignalRoute.CheekFeature, strict: true),
            Pair("DimpleToCornerPull", UnifiedExpressions.MouthDimpleLeft, UnifiedExpressions.MouthDimpleRight, UnifiedExpressions.MouthCornerPullLeft, UnifiedExpressions.MouthCornerPullRight, dimple, cornerPull, SignalRoute.DimpleFeature, strict: false),
            Pair("NoseSneerToUpperLip", UnifiedExpressions.NoseSneerLeft, UnifiedExpressions.NoseSneerRight, UnifiedExpressions.MouthUpperUpLeft, UnifiedExpressions.MouthUpperUpRight, nose, upperLip, SignalRoute.NoseMouthFeature, strict: true),
            Pair("UpperLipToNoseSneer", UnifiedExpressions.MouthUpperUpLeft, UnifiedExpressions.MouthUpperUpRight, UnifiedExpressions.NoseSneerLeft, UnifiedExpressions.NoseSneerRight, upperLip, nose, SignalRoute.NoseMouthFeature, strict: true),
            Pair("BrowLowererToPinch", UnifiedExpressions.BrowLowererLeft, UnifiedExpressions.BrowLowererRight, UnifiedExpressions.BrowPinchLeft, UnifiedExpressions.BrowPinchRight, lowerer, pinch, SignalRoute.BrowFeature, strict: true),
            Pair("BrowPinchToLowerer", UnifiedExpressions.BrowPinchLeft, UnifiedExpressions.BrowPinchRight, UnifiedExpressions.BrowLowererLeft, UnifiedExpressions.BrowLowererRight, pinch, lowerer, SignalRoute.BrowFeature, strict: true),
            new SignalRoute("EyeSquintFromLid", SignalRouteKind.EyeSquintFromLid, SourceLeft: null, SourceRight: null, UnifiedExpressions.EyeSquintLeft, UnifiedExpressions.EyeSquintRight, [], eyeSquint, SignalRoute.EyeLidFeature, Strict: true),
            new SignalRoute("EyeWideFromLid", SignalRouteKind.EyeWideFromLid, SourceLeft: null, SourceRight: null, UnifiedExpressions.EyeWideLeft, UnifiedExpressions.EyeWideRight, [], eyeWide, SignalRoute.EyeLidFeature, Strict: true),
        ];
    }

    private static SignalRoute Pair(string id, UnifiedExpressions sourceLeft, UnifiedExpressions sourceRight, UnifiedExpressions targetLeft, UnifiedExpressions targetRight, string[] sourceNames, string[] targetNames, int feature, bool strict) =>
        new(id, SignalRouteKind.Copy, sourceLeft, sourceRight, targetLeft, targetRight, sourceNames, targetNames, feature, strict);

    private bool Enabled(int feature) => feature switch
    {
        SignalRoute.CheekFeature => CheekToEyeSquint,
        SignalRoute.DimpleFeature => DimpleToSmile,
        SignalRoute.NoseMouthFeature => NoseMouthFill,
        SignalRoute.BrowFeature => BrowFill,
        SignalRoute.EyeLidFeature => EyeLidFill,
        _ => false,
    };

    private bool AnyEnabled() => CheekToEyeSquint || DimpleToSmile || NoseMouthFill || BrowFill || EyeLidFill;

    private float StrengthOf(int feature) => feature switch
    {
        SignalRoute.CheekFeature => CheekStrength,
        SignalRoute.DimpleFeature => DimpleStrength,
        SignalRoute.NoseMouthFeature => NoseMouthStrength,
        SignalRoute.BrowFeature => BrowStrength,
        SignalRoute.EyeLidFeature => EyeLidStrength,
        _ => 0f,
    };

    private AvatarPlan PlanFor(AvatarParameterSnapshot avatar)
    {
        AvatarPlan? plan = Volatile.Read(ref _plan);
        if (plan != null && ReferenceEquals(plan.Avatar, avatar))
        {
            return plan;
        }

        plan = new AvatarPlan(avatar);
        Volatile.Write(ref _plan, plan);
        return plan;
    }

    private void Copy(UnifiedTrackingData data, SignalCapabilitySnapshot capabilities, SignalRoute route, UnifiedExpressions source, UnifiedExpressions target, float strength)
    {
        if (route.Strict && !capabilities.IsLive(source))
        {
            return;
        }

        Raise(data, target, data.Shapes[(int)source].Weight * strength, route.Feature);
    }

    private void FromLid(UnifiedTrackingData data, UnifiedExpressions target, float openness, float rest, bool live, bool wide, float strength, int feature)
    {
        if (!live)
        {
            return;
        }

        float shape = wide ? WideShape(openness, rest) : SquintShape(openness, rest);
        Raise(data, target, shape * strength, feature);
    }

    private void Raise(UnifiedTrackingData data, UnifiedExpressions target, float value, int feature)
    {
        float routed = Math.Clamp(value, 0f, 1f);
        if (routed > data.Shapes[(int)target].Weight)
        {
            data.Shapes[(int)target].Weight = routed;
            MarkTriggered(feature);
        }
    }

    private sealed class AvatarPlan
    {
        public AvatarPlan(AvatarParameterSnapshot avatar)
        {
            Avatar = avatar;
            LacksSource = new bool[Routes.Count];
            DrivesTarget = new bool[Routes.Count];
            for (int i = 0; i < Routes.Count; i++)
            {
                LacksSource[i] = !avatar.DrivesAny(Routes[i].SourceNames);
                DrivesTarget[i] = avatar.DrivesAny(Routes[i].TargetNames);
            }
        }

        public AvatarParameterSnapshot Avatar { get; }

        public bool[] LacksSource { get; }

        public bool[] DrivesTarget { get; }
    }
}
