using System.ComponentModel;
using System.Globalization;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public sealed class MutationProperty : IMutationComponent, INotifyPropertyChanged
{
    private readonly Action<object> _updateField;
    private object _value;

    public MutationProperty(string name, object value, MutationPropertyType type, Action<object> updateField, float min, float max)
    {
        Name = name;
        _value = value;
        Type = type;
        _updateField = updateField;
        Min = min;
        Max = max;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public float Min { get; }

    public float Max { get; }

    public object Value
    {
        get => _value;
        set
        {
            if (Equals(_value, value))
            {
                return;
            }

            _value = value;
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(BoolValue));
            OnPropertyChanged(nameof(FloatValue));
            OnPropertyChanged(nameof(StringValue));
            _updateField.Invoke(_value);
        }
    }

    public bool BoolValue
    {
        get => _value is true;
        set => Value = value;
    }

    public float FloatValue
    {
        get => _value switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            _ => 0f,
        };
        set => Value = value;
    }

    public string StringValue
    {
        get => Convert.ToString(_value, CultureInfo.InvariantCulture) ?? string.Empty;
        set => Value = value;
    }

    public string Name { get; }

    public bool IsDeveloper { get; set; }

    public string? Group { get; set; }

    public bool StartsGroup { get; set; }

    public bool Indented { get; set; }

    public MutationPropertyType Type { get; set; }

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
