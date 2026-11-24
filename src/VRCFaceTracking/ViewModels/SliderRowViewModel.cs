using System.ComponentModel;
using VRCFaceTracking.Core.Params.Data.Mutation;

namespace VRCFaceTracking.ViewModels;

internal sealed class SliderRowViewModel : SettingRowViewModel
{
    private static readonly PropertyChangedEventArgs ValueArgs = new(nameof(Value));

    private readonly MutationProperty _property;

    public SliderRowViewModel(TrackingCardViewModel card, MutationProperty property)
        : base(card, property)
    {
        _property = property;
        Minimum = property.Min;
        Maximum = property.Max > property.Min ? property.Max : property.Min + 1;
        Step = Metadata?.Step is > 0 and var step ? step : 0.01;
    }

    public double Minimum { get; }

    public double Maximum { get; }

    public double Step { get; }

    public double Value
    {
        get => _property.FloatValue;
        set
        {
            double clamped = Math.Clamp(Controls.NumberField.Snap(value, Minimum, Maximum, Step), Minimum, Maximum);
            if (Math.Abs(clamped - _property.FloatValue) < 1e-6)
            {
                OnPropertyChanged(ValueArgs);
                return;
            }

            _property.FloatValue = (float)clamped;
        }
    }

    protected override void OnValueChanged() => OnPropertyChanged(ValueArgs);
}
