using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;

namespace VRCFaceTracking.ViewModels;

public class MutatorViewModel : ObservableRecipient
{
    private readonly ILocalSettingsService _localSettingsService;

    public ObservableCollection<TrackingMutation> Mutations { get; } = new();

    public DeveloperSettings Developer
    {
        get;
    }

    public bool DeveloperMode
    {
        get => Developer.Enabled;
        set
        {
            Developer.Enabled = value;
            _ = _localSettingsService.SaveSettingAsync(DeveloperSettings.SettingKey, value);
            OnPropertyChanged();
        }
    }

    public MutatorViewModel(UnifiedTrackingMutator trackingMutator, DeveloperSettings developer, ILocalSettingsService localSettingsService)
    {
        Developer = developer;
        _localSettingsService = localSettingsService;

        foreach (var mutation in trackingMutator._mutations)
        {
            Mutations.Add(mutation);
        }

        trackingMutator._mutations.CollectionChanged += OnSourceCollectionChanged;
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.NewItems != null)
                foreach (TrackingMutation m in e.NewItems)
                    Mutations.Add(m);

            if (e.OldItems != null)
                foreach (TrackingMutation m in e.OldItems)
                    Mutations.Remove(m);
        });
    }
}
