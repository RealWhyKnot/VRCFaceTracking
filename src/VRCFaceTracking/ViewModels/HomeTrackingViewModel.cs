using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Strings;

namespace VRCFaceTracking.ViewModels;

internal sealed class HomeTrackingViewModel : ObservableObject
{
    private static readonly PropertyChangedEventArgs ActivityTextArgs = new(nameof(ActivityText));
    private static readonly PropertyChangedEventArgs ActivityKindArgs = new(nameof(ActivityKind));

    public OwnerRowViewModel Eyes { get; } = new(Resources.HomeOwner_Eyes);

    public OwnerRowViewModel Mouth { get; } = new(Resources.HomeOwner_Mouth);

    public string ActivityText { get; private set; } = Resources.HomeActivity_NoData;

    public ChipKind ActivityKind { get; private set; }

    public void Refresh(TrackingOwnershipSnapshot ownership, TrackingActivity activity)
    {
        Eyes.Update(ownership.Eyes);
        Mouth.Update(ownership.Mouth);

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
