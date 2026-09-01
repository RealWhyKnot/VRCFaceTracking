using System.Collections.ObjectModel;

namespace VRCFaceTracking.Helpers;

public class BoundedObservableCollection<T>(int capacity) : ObservableCollection<T>
{
    public int Capacity { get; } = capacity;

    protected override void InsertItem(int index, T item)
    {
        base.InsertItem(index, item);
        while (Count > Capacity)
        {
            RemoveAt(0);
        }
    }
}
