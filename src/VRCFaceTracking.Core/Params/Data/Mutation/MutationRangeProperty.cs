using System.ComponentModel;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public sealed class MutationRangeProperty : IMutationComponent, INotifyPropertyChanged
{
    private readonly Action<(float, float)> _updateField;
    private float _item1;
    private float _item2;

    public MutationRangeProperty(string name, float item1, float item2, Action<(float, float)> updateField, float min, float max)
    {
        Name = name;
        _item1 = item1;
        _item2 = item2;
        _updateField = updateField;
        Min = min;
        Max = max;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public float Min { get; }

    public float Max { get; }

    public float Item1
    {
        get => _item1;
        set
        {
            if (_item1 == value)
            {
                return;
            }

            _item1 = value;
            OnPropertyChanged(nameof(Item1));
            _updateField.Invoke((_item1, _item2));
        }
    }

    public float Item2
    {
        get => _item2;
        set
        {
            if (_item2 == value)
            {
                return;
            }

            _item2 = value;
            OnPropertyChanged(nameof(Item2));
            _updateField.Invoke((_item1, _item2));
        }
    }

    public string Name { get; }

    internal void Refresh((float, float) value)
    {
        if (_item1 != value.Item1)
        {
            _item1 = value.Item1;
            OnPropertyChanged(nameof(Item1));
        }

        if (_item2 != value.Item2)
        {
            _item2 = value.Item2;
            OnPropertyChanged(nameof(Item2));
        }
    }

    public string? Group { get; set; }

    public bool StartsGroup { get; set; }

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
