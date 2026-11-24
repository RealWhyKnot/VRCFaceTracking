using System.ComponentModel;
using System.Globalization;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Strings;

namespace VRCFaceTracking.ViewModels;

internal sealed class ToggleRowViewModel : SettingRowViewModel
{
    private static readonly PropertyChangedEventArgs IsCheckedArgs = new(nameof(IsChecked));
    private static readonly PropertyChangedEventArgs StateTextArgs = new(nameof(StateText));
    private static readonly PropertyChangedEventArgs StateTipArgs = new(nameof(StateTip));
    private static readonly PropertyChangedEventArgs HasStateArgs = new(nameof(HasState));
    private static readonly PropertyChangedEventArgs IsStateActiveArgs = new(nameof(IsStateActive));

    private static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(2);

    private readonly MutationProperty _property;

    public ToggleRowViewModel(TrackingCardViewModel card, MutationProperty property, bool showsContinuousState)
        : base(card, property)
    {
        _property = property;
        ReportsTriggers = Unit != null && Metadata != null && Unit.ReportsTriggers(Metadata.Key);
        ShowsContinuousState = showsContinuousState && !ReportsTriggers;
    }

    public bool ReportsTriggers { get; }

    public bool ShowsContinuousState { get; }

    public bool IsTopLevel => DependsOn == null;

    public bool IsChecked
    {
        get => UnitOn && _property.BoolValue;
        set
        {
            if (value == IsChecked)
            {
                return;
            }

            if (value && Unit != null && !UnitOn)
            {
                foreach (ToggleRowViewModel sibling in Card.Toggles)
                {
                    if (!ReferenceEquals(sibling, this) && ReferenceEquals(sibling.Unit, Unit) && sibling.IsTopLevel)
                    {
                        sibling._property.BoolValue = false;
                    }
                }

                Unit.SetCardOn(Card.Card, enabled: true);
            }

            _property.BoolValue = value;
            Card.Refresh();
        }
    }

    public override bool IsEnabled => Card.Page.MasterOn && Card.IsOn && (Parent == null || Parent.IsChecked);

    public bool HasState => StateText != null;

    public string? StateText { get; private set; }

    public string? StateTip { get; private set; }

    public bool IsStateActive { get; private set; }

    public void RefreshState(DateTimeOffset now)
    {
        string? text = null;
        string? tip = null;
        bool active = false;
        if (IsChecked && Card.IsOn && Card.Page.MasterOn)
        {
            if (ReportsTriggers)
            {
                DateTimeOffset? last = Unit!.LastTriggeredFor(Metadata!.Key);
                if (last is not { } when)
                {
                    text = Resources.TrackingRow_Watching;
                }
                else if (now - when < ActiveWindow)
                {
                    text = Resources.TrackingRow_ActiveNow;
                    active = true;
                }
                else
                {
                    text = TrackingText.Format(Resources.TrackingRow_LastActive, TrackingText.Ago(when, now));
                    tip = when.ToLocalTime().ToString("F", CultureInfo.CurrentCulture);
                }
            }
            else if (ShowsContinuousState)
            {
                text = Resources.TrackingRow_AlwaysOn;
            }
        }

        if (!string.Equals(text, StateText, StringComparison.Ordinal))
        {
            bool had = HasState;
            StateText = text;
            OnPropertyChanged(StateTextArgs);
            if (had != HasState)
            {
                OnPropertyChanged(HasStateArgs);
            }
        }

        if (!string.Equals(tip, StateTip, StringComparison.Ordinal))
        {
            StateTip = tip;
            OnPropertyChanged(StateTipArgs);
        }

        if (active != IsStateActive)
        {
            IsStateActive = active;
            OnPropertyChanged(IsStateActiveArgs);
        }
    }

    public override void Refresh()
    {
        OnPropertyChanged(IsCheckedArgs);
        base.Refresh();
    }

    protected override void OnValueChanged() => Card.Refresh();
}
