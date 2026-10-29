using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;

namespace VRCFaceTracking.ViewModels;

internal sealed class MutatorViewModel : ObservableRecipient
{
    public ObservableCollection<TrackingMutation> Mutations { get; } = [];

    public DeveloperSettings Developer
    {
        get;
    }

    public MutatorViewModel(UnifiedTrackingMutator trackingMutator, DeveloperSettings developer)
    {
        Developer = developer;

        foreach (TrackingMutation mutation in trackingMutator.Mutations)
        {
            Show(mutation);
        }

        trackingMutator.Mutations.CollectionChanged += OnSourceCollectionChanged;
    }

    private void Show(TrackingMutation mutation)
    {
        int index = 0;
        while (index < Mutations.Count && Mutations[index].PageOrder <= mutation.PageOrder)
        {
            index++;
        }

        Mutations.Insert(index, mutation);
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                Mutations.Clear();
            }

            if (e.NewItems != null)
            {
                foreach (TrackingMutation m in e.NewItems)
                {
                    Show(m);
                }
            }

            if (e.OldItems != null)
            {
                foreach (TrackingMutation m in e.OldItems)
                {
                    Mutations.Remove(m);
                }
            }
        });
    }
}
