using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Services;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Strings;

namespace VRCFaceTracking.ViewModels;

internal sealed class HomeTrackingViewModel : ObservableObject
{
    private static readonly PropertyChangedEventArgs ActivityTextArgs = new(nameof(ActivityText));
    private static readonly PropertyChangedEventArgs ActivityKindArgs = new(nameof(ActivityKind));
    private static readonly PropertyChangedEventArgs GapTextArgs = new(nameof(GapText));
    private static readonly PropertyChangedEventArgs HasGapArgs = new(nameof(HasGap));
    private static readonly PropertyChangedEventArgs ExternalTrackerTextArgs = new(nameof(ExternalTrackerText));
    private static readonly PropertyChangedEventArgs HasExternalTrackerArgs = new(nameof(HasExternalTracker));

    public OwnerRowViewModel Eyes { get; } = new(Resources.HomeOwner_Eyes);

    public OwnerRowViewModel Mouth { get; } = new(Resources.HomeOwner_Mouth);

    public string ActivityText { get; private set; } = Resources.HomeActivity_NoData;

    public ChipKind ActivityKind { get; private set; }

    public string GapText { get; private set; } = string.Empty;

    public bool HasGap => GapText.Length > 0;

    public string ExternalTrackerText { get; private set; } = string.Empty;

    public bool HasExternalTracker => ExternalTrackerText.Length > 0;

    public void Refresh(TrackingOwnershipSnapshot ownership, TrackingActivity activity, AvatarGapReport? gap = null, IReadOnlyList<DetectedExternalTracker>? external = null)
    {
        Eyes.Update(ownership.Eyes);
        Mouth.Update(ownership.Mouth);
        RefreshExternal(external);

        (string text, ChipKind kind) = Describe(activity);
        if (!string.Equals(text, ActivityText, StringComparison.Ordinal))
        {
            ActivityText = text;
            OnPropertyChanged(ActivityTextArgs);
        }

        if (kind != ActivityKind)
        {
            ActivityKind = kind;
            OnPropertyChanged(ActivityKindArgs);
        }

        string gapText = AvatarGapText.Describe(gap) ?? string.Empty;
        if (!string.Equals(gapText, GapText, StringComparison.Ordinal))
        {
            bool hadGap = HasGap;
            GapText = gapText;
            OnPropertyChanged(GapTextArgs);
            if (hadGap != HasGap)
            {
                OnPropertyChanged(HasGapArgs);
            }
        }
    }

    private void RefreshExternal(IReadOnlyList<DetectedExternalTracker>? external)
    {
        string text = external is { Count: > 0 }
            ? TrackingText.Format(Resources.HomeExternal_Detected, string.Join(", ", external.Select(t => t.Name)))
            : string.Empty;
        if (string.Equals(text, ExternalTrackerText, StringComparison.Ordinal))
        {
            return;
        }

        bool had = HasExternalTracker;
        ExternalTrackerText = text;
        OnPropertyChanged(ExternalTrackerTextArgs);
        if (had != HasExternalTracker)
        {
            OnPropertyChanged(HasExternalTrackerArgs);
        }
    }

    public static (string Text, ChipKind Kind) Describe(TrackingActivity activity)
    {
        if (activity.VrChatNativeFaceTrackingActive)
        {
            return (Resources.HomeActivity_PausedForVrChat, ChipKind.Neutral);
        }

        return activity.State switch
        {
            TrackingActivityState.Active => (Resources.HomeActivity_Tracking, ChipKind.Success),
            TrackingActivityState.Idle => (Resources.HomeActivity_NoNewData, ChipKind.Neutral),
            TrackingActivityState.NoData => (Resources.HomeActivity_NoData, ChipKind.Neutral),
            _ => (Resources.HomeActivity_NoData, ChipKind.Neutral),
        };
    }
}
