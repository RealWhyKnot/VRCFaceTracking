using System.Collections.ObjectModel;

namespace VRCFaceTracking.Helpers;

internal sealed class BoundedObservableCollection<T> : ObservableCollection<T>
{
    public BoundedObservableCollection(int capacity)
    {
        Capacity = capacity;
    }

    public int Capacity { get; }

    protected override void InsertItem(int index, T item)
    {
        base.InsertItem(index, item);
        while (Count > Capacity)
        {
            RemoveAt(0);
        }
    }
}
