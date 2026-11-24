using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public sealed partial class Correctors : TrackingMutation
{
    public override string Name => "Unified Correctors";
    public override string Description => "Processes data to conform to Unified Expressions.";
    public override MutationPriority Priority => MutationPriority.Postprocessor;
    public override int PageOrder => 6;
    public override bool IsActive { get; set; } = true;

    [MutationProperty("MouthClosed/JawOpen Clamp", true, Group = "Mouth")]
    public bool MouthClosedFix { get; set; } = true;
    [MutationProperty("Jaw Reduces When Mouth Closed", true, Group = "Mouth", Indented = true)]
    public bool MouthCloseCompensation { get; set; }
    [MutationProperty("LipSuck Limiter", true, Group = "Mouth")]
    public bool LipSuckFix { get; set; } = true;

    [MutationProperty("EyeLook Symmetrize", true, Group = "Eyes")]
    public bool EyeLookSymmetrize { get; set; }

    internal const string AssistsSettingName = "Mouth & Brow Assists";

    internal sealed class SavedCompensation
    {
        public bool? MouthCloseCompensation { get; set; }
    }

    internal async Task MoveCompensationFromAssistsAsync(ILocalSettingsService settings, CancellationToken cancellationToken)
    {
        SavedCompensation? saved = await settings.ReadSettingAsync<SavedCompensation>(Name, defaultValue: null, forceLocal: true, cancellationToken).ConfigureAwait(false);
        if (saved?.MouthCloseCompensation != null)
        {
            return;
        }

        SavedCompensation? old = await settings.ReadSettingAsync<SavedCompensation>(AssistsSettingName, defaultValue: null, forceLocal: true, cancellationToken).ConfigureAwait(false);
        if (old?.MouthCloseCompensation is not { } enabled)
        {
            return;
        }

        MouthCloseCompensation = enabled;
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        LogCompensationMoved(Logger, enabled, AssistsSettingName, Name);
    }

    public override void MutateData(ref UnifiedTrackingData data)
    {
        if (MouthClosedFix)
        {
            data.Shapes[(int)UnifiedExpressions.MouthClosed].Weight =
                Math.Min(
                    data.Shapes[(int)UnifiedExpressions.MouthClosed].Weight,
                    data.Shapes[(int)UnifiedExpressions.JawOpen].Weight
                );
        }

        if (MouthCloseCompensation)
        {
            float jaw = data.Shapes[(int)UnifiedExpressions.JawOpen].Weight;
            float closed = data.Shapes[(int)UnifiedExpressions.MouthClosed].Weight;
            float reduced = Math.Max(0f, jaw - (0.6f * closed));
            data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = reduced;
            data.Shapes[(int)UnifiedExpressions.MouthClosed].Weight = Math.Min(closed, reduced);
        }

        if (LipSuckFix)
        {
            data.Shapes[(int)UnifiedExpressions.LipSuckLowerLeft].Weight *= 1f - data.Shapes[(int)UnifiedExpressions.MouthLowerDownLeft].Weight;
            data.Shapes[(int)UnifiedExpressions.LipSuckLowerRight].Weight *= 1f - data.Shapes[(int)UnifiedExpressions.MouthLowerDownRight].Weight;
            data.Shapes[(int)UnifiedExpressions.LipSuckUpperLeft].Weight *= 1f - data.Shapes[(int)UnifiedExpressions.MouthUpperUpLeft].Weight;
            data.Shapes[(int)UnifiedExpressions.LipSuckUpperRight].Weight *= 1f - data.Shapes[(int)UnifiedExpressions.MouthUpperUpRight].Weight;
        }

        if (EyeLookSymmetrize)
        {
            float combinedY = (data.Eye.Right.Gaze.y + data.Eye.Left.Gaze.y) / 2;
            data.Eye.Right.Gaze.y = combinedY;
            data.Eye.Left.Gaze.y = combinedY;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Moved Mouth Close Compensation {Enabled} from {From} to {To}")]
    private static partial void LogCompensationMoved(ILogger logger, bool enabled, string from, string to);
}
