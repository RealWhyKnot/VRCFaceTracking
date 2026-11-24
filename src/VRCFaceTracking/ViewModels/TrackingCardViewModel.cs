using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Strings;

namespace VRCFaceTracking.ViewModels;

internal sealed class TrackingCardViewModel : ObservableObject
{
    private static readonly PropertyChangedEventArgs IsOnArgs = new(nameof(IsOn));
    private static readonly PropertyChangedEventArgs ChipTextArgs = new(nameof(ChipText));
    private static readonly PropertyChangedEventArgs ChipKindArgs = new(nameof(ChipKind));
    private static readonly PropertyChangedEventArgs ProgressArgs = new(nameof(Progress));
    private static readonly PropertyChangedEventArgs ShowProgressArgs = new(nameof(ShowProgress));
    private static readonly PropertyChangedEventArgs IsMoreOpenArgs = new(nameof(IsMoreOpen));
    private static readonly PropertyChangedEventArgs MoreLabelArgs = new(nameof(MoreLabel));
    private static readonly PropertyChangedEventArgs HasMoreArgs = new(nameof(HasMore));
    private static readonly PropertyChangedEventArgs ShowMoreArgs = new(nameof(ShowMore));
    private static readonly PropertyChangedEventArgs CanToggleArgs = new(nameof(CanToggle));

    public TrackingCardViewModel(MutatorViewModel page, TrackingCard card, IReadOnlyList<TrackingMutation> units)
    {
        Page = page;
        Card = card;
        Units = units;
        string fallbackTitle = units.Count > 0 ? units[0].Name : card.ToString();
        Title = TrackingText.Get($"TrackingCard_{card}_Title", fallbackTitle);
        Description = TrackingText.Get($"TrackingCard_{card}_Description", units.Count == 1 ? units[0].Description : string.Empty);
        SwitchName = TrackingText.Format(Resources.TrackingCard_SwitchName, Title);
        CollapsesAll = card == TrackingCard.Advanced;
        BuildRows();
        RefreshStatus(DateTimeOffset.UtcNow);
    }

    public MutatorViewModel Page { get; }

    public TrackingCard Card { get; }

    public IReadOnlyList<TrackingMutation> Units { get; }

    public string Title { get; }

    public string Description { get; }

    public bool HasDescription => Description.Length > 0;

    public string SwitchName { get; }

    public bool CollapsesAll { get; }

    public List<SettingRowViewModel> Rows { get; } = [];

    public List<ToggleRowViewModel> Toggles { get; } = [];

    public List<SettingSectionViewModel> MainSections { get; } = [];

    public List<SettingSectionViewModel> MoreSections { get; } = [];

    public bool IsOn
    {
        get
        {
            foreach (TrackingMutation unit in Units)
            {
                if (unit.IsCardOn(Card))
                {
                    return true;
                }
            }

            return false;
        }
        set
        {
            if (value == IsOn)
            {
                return;
            }

            foreach (TrackingMutation unit in Units)
            {
                unit.SetCardOn(Card, value);
            }

            Refresh();
        }
    }

    public bool CanToggle => Page.MasterOn;

    public string ChipText { get; private set; } = string.Empty;

    public ChipKind ChipKind { get; private set; }

    public double Progress { get; private set; }

    public bool ShowProgress { get; private set; }

    public bool HasMore => MoreSections.Exists(section => section.IsVisible);

    public bool ShowMore => HasMore && IsMoreOpen;

    public bool IsMoreOpen
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnPropertyChanged(IsMoreOpenArgs);
            OnPropertyChanged(MoreLabelArgs);
            OnPropertyChanged(ShowMoreArgs);
        }
    }

    public string MoreLabel => CollapsesAll
        ? IsMoreOpen ? Resources.TrackingCard_HideRanges : Resources.TrackingCard_ShowRanges
        : IsMoreOpen ? Resources.TrackingCard_Fewer : Resources.TrackingCard_More;

    public void Refresh()
    {
        OnPropertyChanged(IsOnArgs);
        OnPropertyChanged(CanToggleArgs);
        foreach (SettingRowViewModel row in Rows)
        {
            row.Refresh();
        }

        RefreshVisibility();
        RefreshStatus(DateTimeOffset.UtcNow);
    }

    public void Detach()
    {
        foreach (SettingRowViewModel row in Rows)
        {
            row.Detach();
        }
    }

    public void RefreshVisibility()
    {
        foreach (SettingSectionViewModel section in MainSections)
        {
            section.Refresh();
        }

        foreach (SettingSectionViewModel section in MoreSections)
        {
            section.Refresh();
        }

        OnPropertyChanged(HasMoreArgs);
        OnPropertyChanged(ShowMoreArgs);
    }

    public void RefreshStatus() => RefreshStatus(DateTimeOffset.UtcNow);

    public void RefreshStatus(DateTimeOffset now)
    {
        foreach (ToggleRowViewModel toggle in Toggles)
        {
            toggle.RefreshState(now);
        }

        (string text, ChipKind kind, double progress, bool showProgress) = ComputeChip();
        if (!string.Equals(text, ChipText, StringComparison.Ordinal))
        {
            ChipText = text;
            OnPropertyChanged(ChipTextArgs);
        }

        if (kind != ChipKind)
        {
            ChipKind = kind;
            OnPropertyChanged(ChipKindArgs);
        }

        if (Math.Abs(progress - Progress) > 0.001)
        {
            Progress = progress;
            OnPropertyChanged(ProgressArgs);
        }

        if (showProgress != ShowProgress)
        {
            ShowProgress = showProgress;
            OnPropertyChanged(ShowProgressArgs);
        }
    }

    private (string Text, ChipKind Kind, double Progress, bool ShowProgress) ComputeChip()
    {
        if (!IsOn)
        {
            return (Resources.TrackingChip_Off, ChipKind.Neutral, 0, false);
        }

        if (!Page.MasterOn)
        {
            return (Resources.TrackingChip_Paused, ChipKind.Neutral, 0, false);
        }

        foreach (TrackingMutation unit in Units)
        {
            if (unit is Calibration calibration && Card == TrackingCard.Calibration)
            {
                return CalibrationChip(calibration.Status);
            }

            if (unit is CheekDimpleFill fill && Card == TrackingCard.FillIns)
            {
                return FillChip(fill.FillState);
            }
        }

        return Card == TrackingCard.Advanced ? RangeChip() : CountChip();
    }

    private static (string Text, ChipKind Kind, double Progress, bool ShowProgress) CalibrationChip(CalibrationStatus status) => status.State switch
    {
        CalibrationState.Learning => (
            TrackingText.Format(Resources.TrackingChip_Learning, ((int)Math.Floor(status.Progress * 100)).ToString(CultureInfo.CurrentCulture)),
            ChipKind.Progress,
            status.Progress,
            status.Progress > 0),
        CalibrationState.Ready => (Resources.TrackingChip_Ready, ChipKind.Success, 1, false),
        CalibrationState.Off => (Resources.TrackingChip_Off, ChipKind.Neutral, 0, false),
        _ => (Resources.TrackingChip_Off, ChipKind.Neutral, 0, false),
    };

    private static (string Text, ChipKind Kind, double Progress, bool ShowProgress) FillChip(FillInState state) => state switch
    {
        FillInState.WaitingForAvatar => (Resources.TrackingChip_WaitingForAvatar, ChipKind.Neutral, 0, false),
        FillInState.NativeFaceTracking => (Resources.TrackingChip_PausedForVrChat, ChipKind.Neutral, 0, false),
        FillInState.NotNeeded => (Resources.TrackingChip_NotNeeded, ChipKind.Neutral, 0, false),
        FillInState.Filling => (Resources.TrackingChip_Filling, ChipKind.Success, 0, false),
        FillInState.Off => (Resources.TrackingChip_Off, ChipKind.Neutral, 0, false),
        _ => (Resources.TrackingChip_Off, ChipKind.Neutral, 0, false),
    };

    private (string Text, ChipKind Kind, double Progress, bool ShowProgress) RangeChip()
    {
        int changed = 0;
        foreach (SettingRowViewModel row in Rows)
        {
            if (row is RangeRowViewModel { IsDefault: false })
            {
                changed++;
            }
        }

        return changed == 0
            ? (Resources.TrackingChip_DefaultRanges, ChipKind.Neutral, 0, false)
            : (TrackingText.Format(Resources.TrackingChip_Changed, changed), ChipKind.Success, 0, false);
    }

    private (string Text, ChipKind Kind, double Progress, bool ShowProgress) CountChip()
    {
        int total = 0;
        int on = 0;
        foreach (ToggleRowViewModel toggle in Toggles)
        {
            if (toggle.IsTopLevel && toggle.IsVisible)
            {
                total++;
                if (toggle.IsChecked)
                {
                    on++;
                }
            }
        }

        return total == 0
            ? (Resources.TrackingChip_On, ChipKind.Success, 0, false)
            : (TrackingText.Format(Resources.TrackingChip_Count, on, total), on > 0 ? ChipKind.Success : ChipKind.Neutral, 0, false);
    }

    private void BuildRows()
    {
        bool titled = Card is TrackingCard.Eyes or TrackingCard.Advanced or TrackingCard.Other;
        foreach ((SettingRowViewModel row, string? group) in CollectRows())
        {
            Rows.Add(row);
            if (row is ToggleRowViewModel toggle)
            {
                Toggles.Add(toggle);
            }

            List<SettingSectionViewModel> area = CollapsesAll || row.IsAdvanced ? MoreSections : MainSections;
            string key = group ?? string.Empty;
            SettingSectionViewModel? section = area.Find(candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));
            if (section == null)
            {
                string? title = titled && group != null ? SectionTitle(group) : null;
                section = new SettingSectionViewModel(key, title, isCollapsible: Card == TrackingCard.Advanced);
                area.Add(section);
            }

            section.Rows.Add(row);
            row.Section = section;
        }

        foreach (SettingRowViewModel row in Rows)
        {
            if (row.DependsOn is { } parentKey)
            {
                row.Parent = Toggles.Find(toggle => ReferenceEquals(toggle.Unit, row.Unit) && string.Equals(toggle.Metadata?.Key, parentKey, StringComparison.Ordinal));
            }
        }
    }

    private List<(SettingRowViewModel Row, string? Group)> CollectRows()
    {
        List<(SettingRowViewModel Row, int Order, int UnitOrder, int UnitIndex, int Index, string? Group)> found = [];
        for (int unitIndex = 0; unitIndex < Units.Count; unitIndex++)
        {
            TrackingMutation unit = Units[unitIndex];
            foreach (IMutationComponent component in unit.Components)
            {
                TrackingCard home = component.Metadata?.Card ?? unit.HomeCard;
                if (home == Card && Create(component) is { } row)
                {
                    string? group = Card == TrackingCard.Other ? unit.Name : component.Group;
                    found.Add((row, component.Metadata?.Order ?? 0, unit.CardOrder, unitIndex, component.Metadata?.Index ?? 0, group));
                }
            }
        }

        found.Sort((a, b) =>
        {
            int result = a.Order.CompareTo(b.Order);
            if (result == 0)
            {
                result = a.UnitOrder.CompareTo(b.UnitOrder);
            }

            if (result == 0)
            {
                result = a.UnitIndex.CompareTo(b.UnitIndex);
            }

            return result == 0 ? a.Index.CompareTo(b.Index) : result;
        });

        return found.ConvertAll(item => (item.Row, item.Group));
    }

    private string SectionTitle(string group) =>
        Card == TrackingCard.Other
            ? group
            : TrackingText.Get("TrackingSection_" + group.Replace(' ', '_'), group);

    private SettingRowViewModel? Create(IMutationComponent component) => component switch
    {
        MutationRangeProperty range => new RangeRowViewModel(this, range),
        MutationAction action => new ActionRowViewModel(this, action),
        MutationProperty { Type: MutationPropertyType.CheckBox } toggle => new ToggleRowViewModel(this, toggle, Card == TrackingCard.AutoFixes),
        MutationProperty { Type: MutationPropertyType.Slider } slider => new SliderRowViewModel(this, slider),
        MutationProperty text => new TextRowViewModel(this, text),
        _ => null,
    };
}
