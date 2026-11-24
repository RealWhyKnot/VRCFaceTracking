using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Strings;

namespace VRCFaceTracking.ViewModels;

internal sealed class MutatorViewModel : ObservableRecipient
{
    private const int StatusEvery = 8;

    private static readonly PropertyChangedEventArgs MasterOnArgs = new(nameof(MasterOn));
    private static readonly PropertyChangedEventArgs HasDataArgs = new(nameof(HasData));
    private static readonly PropertyChangedEventArgs ActivityTextArgs = new(nameof(ActivityText));
    private static readonly PropertyChangedEventArgs ActivityKindArgs = new(nameof(ActivityKind));

    private readonly UnifiedTrackingMutator _mutator;
    private readonly DispatcherTimer _timer;
    private int _tick;

    public MutatorViewModel(UnifiedTrackingMutator trackingMutator, DeveloperSettings developer)
    {
        _mutator = trackingMutator;
        Developer = developer;
        Channels =
        [
            new PreviewChannelViewModel(Resources.TrackingPreview_LeftEye, 0),
            new PreviewChannelViewModel(Resources.TrackingPreview_RightEye, 0),
            new PreviewChannelViewModel(Resources.TrackingPreview_LookSideways, -1),
            new PreviewChannelViewModel(Resources.TrackingPreview_LookUpDown, -1),
            new PreviewChannelViewModel(Resources.TrackingPreview_JawOpen, 0),
            new PreviewChannelViewModel(Resources.TrackingPreview_Smile, 0),
            new PreviewChannelViewModel(Resources.TrackingPreview_EyeSquint, 0),
            new PreviewChannelViewModel(Resources.TrackingPreview_CheekSquint, 0),
        ];

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(60) };
        _timer.Tick += OnTick;
        Rebuild();
        trackingMutator.Mutations.CollectionChanged += OnSourceCollectionChanged;
        trackingMutator.PropertyChanged += OnMutatorChanged;
        developer.PropertyChanged += OnDeveloperChanged;
        RefreshStatus();
    }

    public DeveloperSettings Developer { get; }

    public ObservableCollection<TrackingCardViewModel> Cards { get; } = [];

    public IReadOnlyList<PreviewChannelViewModel> Channels { get; }

    public bool IsRunning => _timer.IsEnabled;

    public bool MasterOn
    {
        get => _mutator.Enabled;
        set
        {
            if (value == _mutator.Enabled)
            {
                return;
            }

            _mutator.Enabled = value;
            _ = _mutator.SaveAsync(CancellationToken.None);
        }
    }

    public bool HasData { get; private set; }

    public string ActivityText { get; private set; } = Resources.TrackingActivity_Waiting;

    public ChipKind ActivityKind { get; private set; }

    public TrackingCardViewModel? FindCard(TrackingCard card)
    {
        foreach (TrackingCardViewModel candidate in Cards)
        {
            if (candidate.Card == card)
            {
                return candidate;
            }
        }

        return null;
    }

    public void Start()
    {
        if (_timer.IsEnabled)
        {
            return;
        }

        _tick = 0;
        RefreshStatus();
        RefreshLive();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void RefreshLive()
    {
        if (!HasData)
        {
            return;
        }

        TrackingPreviewSnapshot snapshot = _mutator.PreviewSnapshot;
        TrackingPreviewValues raw = snapshot.Raw;
        TrackingPreviewValues adjusted = snapshot.Adjusted;
        Channels[0].Set(raw.EyeOpennessLeft, adjusted.EyeOpennessLeft);
        Channels[1].Set(raw.EyeOpennessRight, adjusted.EyeOpennessRight);
        Channels[2].Set(raw.GazeX, adjusted.GazeX);
        Channels[3].Set(raw.GazeY, adjusted.GazeY);
        Channels[4].Set(raw.JawOpen, adjusted.JawOpen);
        Channels[5].Set(raw.MouthSmile, adjusted.MouthSmile);
        Channels[6].Set(raw.EyeSquint, adjusted.EyeSquint);
        Channels[7].Set(raw.CheekSquint, adjusted.CheekSquint);
    }

    public void RefreshStatus()
    {
        TrackingActivity activity = _mutator.Activity;
        (string text, ChipKind kind) = activity.State switch
        {
            TrackingActivityState.Active => (Resources.TrackingActivity_Active, ChipKind.Success),
            TrackingActivityState.Idle => (Resources.TrackingActivity_Idle, ChipKind.Neutral),
            TrackingActivityState.NoData => (Resources.TrackingActivity_Waiting, ChipKind.Neutral),
            _ => (Resources.TrackingActivity_Waiting, ChipKind.Neutral),
        };

        bool hasData = activity.State != TrackingActivityState.NoData;
        if (hasData != HasData)
        {
            HasData = hasData;
            OnPropertyChanged(HasDataArgs);
        }

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

        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (TrackingCardViewModel card in Cards)
        {
            card.RefreshStatus(now);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (++_tick >= StatusEvery)
        {
            _tick = 0;
            RefreshStatus();
        }

        RefreshLive();
    }

    private void Rebuild()
    {
        foreach (TrackingCardViewModel stale in Cards)
        {
            stale.Detach();
        }

        Cards.Clear();
        Dictionary<TrackingCard, List<TrackingMutation>> units = [];
        foreach (TrackingMutation mutation in _mutator.Mutations)
        {
            if (mutation.Components.Count == 0)
            {
                Add(units, mutation.HomeCard, mutation);
            }

            foreach (IMutationComponent component in mutation.Components)
            {
                Add(units, component.Metadata?.Card ?? mutation.HomeCard, mutation);
            }
        }

        List<TrackingCard> order = [.. units.Keys];
        order.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
        foreach (TrackingCard card in order)
        {
            List<TrackingMutation> list = units[card];
            list.Sort((a, b) => a.CardOrder.CompareTo(b.CardOrder));
            Cards.Add(new TrackingCardViewModel(this, card, list));
        }
    }

    private static int Rank(TrackingCard card) =>
        card == TrackingCard.Other
            ? (int)TrackingCard.Advanced
            : card == TrackingCard.Advanced ? (int)TrackingCard.Other : (int)card;

    private static void Add(Dictionary<TrackingCard, List<TrackingMutation>> units, TrackingCard card, TrackingMutation mutation)
    {
        if (!units.TryGetValue(card, out List<TrackingMutation>? list))
        {
            list = [];
            units[card] = list;
        }

        if (!list.Contains(mutation))
        {
            list.Add(mutation);
        }
    }

    private void RefreshCards()
    {
        foreach (TrackingCardViewModel card in Cards)
        {
            card.Refresh();
        }
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(Rebuild);

    private void OnMutatorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(UnifiedTrackingMutator.Enabled), StringComparison.Ordinal))
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            OnMasterChanged();
        }
        else
        {
            Dispatcher.UIThread.Post(OnMasterChanged);
        }
    }

    private void OnMasterChanged()
    {
        OnPropertyChanged(MasterOnArgs);
        RefreshCards();
    }

    private void OnDeveloperChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(DeveloperSettings.Enabled), StringComparison.Ordinal))
        {
            RefreshCards();
        }
    }
}
