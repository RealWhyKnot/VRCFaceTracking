using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

public sealed class LiveStatusTests
{
    private static readonly string[] DrivenParameters = ["v2/CheekSquint", "v2/MouthDimple"];

    [Fact]
    public void CalibrationReportsOffUntilActive()
    {
        var calibration = new Calibration();

        Assert.Equal(CalibrationState.Off, calibration.Status.State);
        Assert.Equal(0, calibration.LearnedShapeCount);
    }

    [Fact]
    public void CalibrationReportsLearningWithShapeCounts()
    {
        var calibration = new Calibration { IsActive = true };
        calibration.Initialize(new UnifiedTrackingData());

        CalibrationStatus status = calibration.Status;

        Assert.Equal(CalibrationState.Learning, status.State);
        Assert.True(status.TotalShapeCount > 0);
        Assert.InRange(status.Progress, 0f, 1f);
        Assert.Equal(calibration.TotalShapeCount, status.TotalShapeCount);
        Assert.False(string.IsNullOrWhiteSpace(status.Text));
    }

    [Fact]
    public void CheekFillReportsEachSituation()
    {
        AvatarParameterSnapshot? avatar = null;
        var fill = new SignalFillIn { CurrentAvatar = () => avatar };

        Assert.Equal("Off", fill.StatusSummary);

        fill.IsActive = true;
        Assert.Equal("Waiting for avatar", fill.StatusSummary);

        avatar = new AvatarParameterSnapshot(usesNativeFaceTracking: true, parameterNames: []);
        Assert.Equal("Skipped: VRChat native face tracking active", fill.StatusSummary);

        avatar = new AvatarParameterSnapshot(usesNativeFaceTracking: false, parameterNames: []);
        Assert.Equal("Active", fill.StatusSummary);

        avatar = new AvatarParameterSnapshot(usesNativeFaceTracking: false, parameterNames: DrivenParameters);
        Assert.Equal("Skipped: avatar already has Cheek & Dimple parameters", fill.StatusSummary);
    }

    [Fact]
    public void CheekFillStampsLastTriggeredOnlyWhenItRoutesAValue()
    {
        var now = new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);
        var fill = new SignalFillIn
        {
            IsActive = true,
            CurrentAvatar = () => new AvatarParameterSnapshot(usesNativeFaceTracking: false, parameterNames: []),
            Clock = new FixedClock(now),
        };
        var data = new UnifiedTrackingData();

        fill.MutateData(ref data);
        Assert.Null(fill.LastTriggeredUtc);

        data.Shapes[(int)UnifiedExpressions.CheekSquintLeft].Weight = 0.8f;
        fill.MutateData(ref data);

        Assert.Equal(now, fill.LastTriggeredUtc);
        Assert.Equal(0.4f, data.Shapes[(int)UnifiedExpressions.EyeSquintLeft].Weight, 3);
    }

    [Fact]
    public void GlitchFixesReportOffWhenNothingIsEnabled()
    {
        var fixes = new TrackingGlitchFixes { IsActive = true, QuietMouth = false, StuckEyelidFallback = false, RelaxOnFreeze = false };

        Assert.Equal("Off", fixes.StatusSummary);

        fixes.QuietMouth = true;
        Assert.Equal("Active", fixes.StatusSummary);
        Assert.Null(fixes.LastTriggeredUtc);
    }

    [Theory]
    [InlineData(0L, 100L, false, TrackingActivityState.NoData)]
    [InlineData(100L, 150L, false, TrackingActivityState.Active)]
    [InlineData(100L, 400L, false, TrackingActivityState.Idle)]
    [InlineData(100L, 400L, true, TrackingActivityState.Idle)]
    public void ActivityIsClassifiedFromTheLastFrame(long last, long now, bool native, TrackingActivityState expected)
    {
        var activity = TrackingActivity.Classify(last, now, ticksPerSecond: 100, native);

        Assert.Equal(expected, activity.State);
        Assert.Equal(native, activity.VrChatNativeFaceTrackingActive);
        Assert.False(string.IsNullOrWhiteSpace(activity.Summary));
    }

    [Fact]
    public void SingleOwnerIsNotAConflict()
    {
        var snapshot = TrackingOwnershipSnapshot.Build([Claim("A", eyes: true, mouth: true)]);

        Assert.Equal("A", snapshot.Eyes.Owner);
        Assert.Equal("A", snapshot.Mouth.Owner);
        Assert.False(snapshot.HasOwnershipConflict);
        Assert.Empty(snapshot.ConflictingModules);
    }

    [Fact]
    public void TwoModulesClaimingEyesIsAConflictNamingBoth()
    {
        var snapshot = TrackingOwnershipSnapshot.Build([Claim("A", eyes: true, mouth: false), Claim("B", eyes: true, mouth: true)]);

        Assert.True(snapshot.Eyes.HasConflict);
        Assert.False(snapshot.Mouth.HasConflict);
        Assert.True(snapshot.HasOwnershipConflict);
        Assert.Equal(["A", "B"], snapshot.ConflictingModules);
        Assert.Equal("A", snapshot.Eyes.Owner);
        Assert.Equal("B", snapshot.Mouth.Owner);
    }

    [Fact]
    public void EmptySnapshotHasNoOwners()
    {
        Assert.Null(TrackingOwnershipSnapshot.Empty.Eyes.Owner);
        Assert.False(TrackingOwnershipSnapshot.Empty.HasOwnershipConflict);
        Assert.Null(TrackingOwnershipSnapshot.Build([]).Mouth.Owner);
        Assert.Empty(TrackingOwnershipSnapshot.Build([]).Mouth.Claimants);
    }

    [Fact]
    public void PreviewSnapshotShowsRawAndAdjustedSeparately()
    {
        var mutator = new UnifiedTrackingMutator(NullLogger<UnifiedTrackingMutator>.Instance, new NullSettings(), new InlineDispatcher());
        var input = new UnifiedTrackingData();
        input.Shapes[(int)UnifiedExpressions.JawOpen].Weight = 0.5f;

        mutator.MutateData(input);
        TrackingPreviewSnapshot snapshot = mutator.PreviewSnapshot;

        Assert.False(snapshot.MutationsApplied);
        Assert.Equal(0.5f, snapshot.Raw.JawOpen, 3);
        Assert.Equal(snapshot.Raw, snapshot.Adjusted);
        Assert.True(snapshot.TimestampTicks > 0);
    }

    [Fact]
    public void PreviewSnapshotKeepsTheRawValueWhenAMutationChangesIt()
    {
        var mutator = new UnifiedTrackingMutator(NullLogger<UnifiedTrackingMutator>.Instance, new NullSettings(), new InlineDispatcher())
        {
            Enabled = true,
        };
        mutator.Mutations.Add(new ParameterAdjustment { IsActive = true, JawOpen = (0f, 0.5f) });
        var input = new UnifiedTrackingData();
        input.Shapes[(int)UnifiedExpressions.JawOpen].Weight = 0.25f;

        mutator.MutateData(input);
        TrackingPreviewSnapshot snapshot = mutator.PreviewSnapshot;

        Assert.True(snapshot.MutationsApplied);
        Assert.Equal(0.25f, snapshot.Raw.JawOpen, 3);
        Assert.Equal(0.5f, snapshot.Adjusted.JawOpen, 3);
    }

    [Fact]
    public void ActivityUsesTheCurrentAvatarForNativeTracking()
    {
        var mutator = new UnifiedTrackingMutator(NullLogger<UnifiedTrackingMutator>.Instance, new NullSettings(), new InlineDispatcher())
        {
            CurrentAvatar = () => new AvatarParameterSnapshot(usesNativeFaceTracking: true, parameterNames: []),
        };

        Assert.True(mutator.Activity.VrChatNativeFaceTrackingActive);
    }

    private static ModuleClaim Claim(string name, bool eyes, bool mouth)
    {
        TrackingCapability effective = (eyes ? TrackingCapability.Eyes : TrackingCapability.None) | (mouth ? TrackingCapability.Mouth : TrackingCapability.None);
        return new ModuleClaim(name, eyes, mouth, AllowedOverride: null, effective);
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

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
}
