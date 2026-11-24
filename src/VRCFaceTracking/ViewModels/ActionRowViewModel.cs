using System.Windows.Input;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Strings;

namespace VRCFaceTracking.ViewModels;

internal sealed class ActionRowViewModel : SettingRowViewModel
{
    private readonly MutationAction _action;

    public ActionRowViewModel(TrackingCardViewModel card, MutationAction action)
        : base(card, action)
    {
        _action = action;
        ButtonText = TrackingText.Get(TextKey + "_Button", Resources.TrackingAction_Run);
        ConfirmText = TrackingText.Get(TextKey + "_Confirm", Resources.TrackingAction_Confirm);
    }

    public string ButtonText { get; }

    public string ConfirmText { get; }

    public bool NeedsConfirm => Metadata?.Confirm == true;

    public ICommand Command => _action;

    public Task RunAsync(CancellationToken cancellationToken) => _action.RunAsync(cancellationToken);

    protected override void OnValueChanged()
    {
    }
}
