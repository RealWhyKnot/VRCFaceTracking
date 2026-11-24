using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

[Collection("UnifiedTrackingState")]
public sealed class MutatorPipelineOrderTests
{
    private const float ExternalOpenness = 0.4f;
    private const float EyeRest = 0.9f;

    private sealed class NullSettings : ILocalSettingsService
    {
        public Task<T?> ReadSettingAsync<T>(string key, T? defaultValue, bool forceLocal, CancellationToken cancellationToken) => Task.FromResult(defaultValue);
        public Task SaveSettingAsync<T>(string key, T value, bool forceLocal, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InlineDispatcher : IDispatcherService
    {
        public void Run(Action action) => action();
    }

    private sealed class ExternalEyes : IExternalTrackingSource, IDisposable
    {
        private readonly ManualResetEvent _arrived = new(initialState: false);

        public int Version => 0;

        public WaitHandle DataArrived => _arrived;

        public TimeSpan? SinceLastData => TimeSpan.Zero;

        public bool DrivesEyes => true;

        public bool DrivesFace => false;

        public List<float> SeenOpenness { get; } = [];

        public void Merge(UnifiedTrackingData data)
        {
            SeenOpenness.Add(data.Eye.Left.Openness);
            data.Eye.Left.Openness = ExternalOpenness;
            data.Eye.Right.Openness = ExternalOpenness;
        }

        public void Dispose() => _arrived.Dispose();
    }

    private static int IndexOf<T>(TrackingMutation[] mutations) => Array.FindIndex(mutations, mutation => mutation is T);

    [Fact]
    public void DiscoveredOrderRunsGlitchFixesThenFillInThenSmoothing()
    {
        TrackingMutation[] mutations = TrackingMutation.GetImplementingMutations(ordered: true);

        int glitch = IndexOf<TrackingGlitchFixes>(mutations);
        int fill = IndexOf<SignalFillIn>(mutations);
        int filter = IndexOf<Filter>(mutations);

        Assert.True(glitch >= 0 && fill >= 0 && filter >= 0);
        Assert.True(glitch < fill);
        Assert.True(fill < filter);
    }

    [Fact]
    public void ExternalMergeFeedsGlitchFixesWhichFeedFillIn()
    {
        using var external = new ExternalEyes();
        var mutator = new UnifiedTrackingMutator(NullLogger<UnifiedTrackingMutator>.Instance, new NullSettings(), new InlineDispatcher(), external) { Enabled = true };
        var frozen = new UnifiedTrackingData();
        frozen.Eye.Left.Openness = 1f;
        frozen.Eye.Right.Openness = 1f;
        double now = 0;
        bool[] none = new bool[(int)UnifiedExpressions.Max];
        var capabilities = new SignalCapabilitySnapshot(none, eyeLeftLive: false, eyeRightLive: false, EyeRest, EyeRest, sampleCount: 100, version: 1, none, externalEyeLeft: true, externalEyeRight: true);
        var avatar = new AvatarParameterSnapshot(usesNativeFaceTracking: false, ["v2/EyeSquintLeft", "v2/EyeSquintRight"]);

        foreach (TrackingMutation mutation in TrackingMutation.GetImplementingMutations(ordered: true))
        {
            switch (mutation)
            {
                case TrackingGlitchFixes fixes:
                    fixes.IsActive = true;
                    fixes.QuietMouth = false;
                    fixes.StuckEyelidFallback = false;
                    fixes.RelaxOnFreeze = true;
                    fixes.NowSeconds = () => now;
                    fixes.RawInput = () => frozen;
                    mutator.Mutations.Add(fixes);
                    break;
                case SignalFillIn fill:
                    fill.IsActive = true;
                    fill.CheekToEyeSquint = false;
                    fill.EyeLidFill = true;
                    fill.EyeLidStrength = 1f;
                    fill.CurrentAvatar = () => avatar;
                    fill.Capabilities = () => capabilities;
                    mutator.Mutations.Add(fill);
                    break;
                default:
                    break;
            }
        }

        Assert.Equal(2, mutator.Mutations.Count);
        UnifiedTrackingData first = mutator.MutateData(frozen);
        Assert.Equal(ExternalOpenness, first.Eye.Left.Openness, 4);
        Assert.Equal(1f, first.Shapes[(int)UnifiedExpressions.EyeSquintLeft].Weight, 3);

        now = 2.5;
        UnifiedTrackingData output = mutator.MutateData(frozen);

        Assert.Equal([1f, 1f], external.SeenOpenness);
        Assert.Equal(0.7f, output.Eye.Left.Openness, 3);
        Assert.Equal(0f, output.Shapes[(int)UnifiedExpressions.EyeSquintLeft].Weight, 3);
        Assert.Equal(0f, output.Shapes[(int)UnifiedExpressions.EyeSquintRight].Weight, 3);
    }
}
