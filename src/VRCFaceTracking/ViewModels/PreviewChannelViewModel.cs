using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VRCFaceTracking.ViewModels;

internal sealed class PreviewChannelViewModel : ObservableObject
{
    private const double Threshold = 0.002;

    private static readonly PropertyChangedEventArgs RawArgs = new(nameof(Raw));
    private static readonly PropertyChangedEventArgs AdjustedArgs = new(nameof(Adjusted));

    public PreviewChannelViewModel(string label, double minimum)
    {
        Label = label;
        Minimum = minimum;
    }

    public string Label { get; }

    public double Minimum { get; }

    public double Maximum { get; } = 1;

    public double Raw { get; private set; }

    public double Adjusted { get; private set; }

    public void Set(double raw, double adjusted)
    {
        raw = Clean(raw);
        adjusted = Clean(adjusted);
        if (Math.Abs(raw - Raw) > Threshold || (raw != Raw && (raw == 0 || raw == Minimum || raw == 1)))
        {
            Raw = raw;
            OnPropertyChanged(RawArgs);
        }

        if (Math.Abs(adjusted - Adjusted) > Threshold || (adjusted != Adjusted && (adjusted == 0 || adjusted == Minimum || adjusted == 1)))
        {
            Adjusted = adjusted;
            OnPropertyChanged(AdjustedArgs);
        }
    }

    private double Clean(double value) => double.IsFinite(value) ? Math.Clamp(value, Minimum, 1) : 0;
}
