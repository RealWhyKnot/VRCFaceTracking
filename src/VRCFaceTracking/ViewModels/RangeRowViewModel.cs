using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCFaceTracking.Controls;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Strings;

namespace VRCFaceTracking.ViewModels;

internal sealed class RangeRowViewModel : SettingRowViewModel
{
    public const double Gap = 0.05;

    private static readonly PropertyChangedEventArgs LowerArgs = new(nameof(Lower));
    private static readonly PropertyChangedEventArgs UpperArgs = new(nameof(Upper));
    private static readonly PropertyChangedEventArgs IsDefaultArgs = new(nameof(IsDefault));

    private readonly MutationRangeProperty _range;
    private readonly (float Lower, float Upper) _default;

    public RangeRowViewModel(TrackingCardViewModel card, MutationRangeProperty range)
        : base(card, range)
    {
        _range = range;
        Minimum = range.Min;
        Maximum = range.Max > range.Min ? range.Max : range.Min + 1;
        Step = Metadata?.Step is > 0 and var step ? step : 0.01;
        _default = Metadata?.DefaultValue is ValueTuple<float, float> value ? value : ((float)Minimum, (float)Maximum);
        ResetCommand = new RelayCommand(Reset);
        LowerName = TrackingText.Format(Resources.TrackingRange_LowerName, Label);
        UpperName = TrackingText.Format(Resources.TrackingRange_UpperName, Label);
        ResetName = TrackingText.Format(Resources.TrackingRange_ResetName, Label);
    }

    public double Minimum { get; }

    public double Maximum { get; }

    public double Step { get; }

    public string LowerName { get; }

    public string UpperName { get; }

    public string ResetName { get; }

    public IRelayCommand ResetCommand { get; }

    public double Lower
    {
        get => _range.Item1;
        set => Apply(value, Upper, movingUpper: false);
    }

    public double Upper
    {
        get => _range.Item2;
        set => Apply(Lower, value, movingUpper: true);
    }

    public bool IsDefault =>
        Math.Abs(_range.Item1 - _default.Lower) < 1e-4 && Math.Abs(_range.Item2 - _default.Upper) < 1e-4;

    public void Reset() => _range.SetRange(_default.Lower, _default.Upper);

    protected override void OnValueChanged()
    {
        OnPropertyChanged(LowerArgs);
        OnPropertyChanged(UpperArgs);
        OnPropertyChanged(IsDefaultArgs);
        Card.RefreshStatus();
    }

    private void Apply(double lower, double upper, bool movingUpper)
    {
        (double low, double high) = RangeSlider.Constrain(lower, upper, Minimum, Maximum, Gap, movingUpper);
        low = Math.Round(low, 4);
        high = Math.Round(high, 4);
        if (Math.Abs(low - _range.Item1) < 1e-6 && Math.Abs(high - _range.Item2) < 1e-6)
        {
            OnPropertyChanged(LowerArgs);
            OnPropertyChanged(UpperArgs);
            return;
        }

        _range.SetRange((float)low, (float)high);
    }
}
