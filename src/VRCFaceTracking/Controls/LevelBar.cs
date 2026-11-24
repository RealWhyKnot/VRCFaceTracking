using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VRCFaceTracking.Controls;

internal sealed class LevelBar : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<LevelBar, double>(nameof(Value));

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<LevelBar, double>(nameof(Minimum));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<LevelBar, double>(nameof(Maximum), 1d);

    public static readonly StyledProperty<bool> IsAdjustedProperty =
        AvaloniaProperty.Register<LevelBar, bool>(nameof(IsAdjusted));

    public static readonly StyledProperty<bool> IsPausedProperty =
        AvaloniaProperty.Register<LevelBar, bool>(nameof(IsPaused));

    private const double BarHeight = 4;

    static LevelBar()
    {
        AffectsRender<LevelBar>(ValueProperty, MinimumProperty, MaximumProperty, IsAdjustedProperty, IsPausedProperty);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public bool IsAdjusted
    {
        get => GetValue(IsAdjustedProperty);
        set => SetValue(IsAdjustedProperty, value);
    }

    public bool IsPaused
    {
        get => GetValue(IsPausedProperty);
        set => SetValue(IsPausedProperty, value);
    }

    public static (double From, double To) Span(double value, double minimum, double maximum)
    {
        double range = maximum - minimum;
        if (range <= 0 || !double.IsFinite(value))
        {
            return (0, 0);
        }

        double t = Math.Clamp((value - minimum) / range, 0, 1);
        if (minimum < 0 && maximum > 0)
        {
            double zero = -minimum / range;
            return t < zero ? (t, zero) : (zero, t);
        }

        return (0, t);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 80 : availableSize.Width, BarHeight);

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width;
        double y = (Bounds.Height - BarHeight) / 2;
        IBrush track = Brush("ControlStrongFillColorDisabledBrush", Brushes.DimGray);
        IBrush fill = IsAdjusted && !IsPaused
            ? Brush("AccentFillColorDefaultBrush", Brushes.DodgerBlue)
            : Brush(IsAdjusted ? "TextFillColorDisabledBrush" : "TextFillColorSecondaryBrush", Brushes.Gray);
        var radius = new CornerRadius(BarHeight / 2);
        context.DrawRectangle(track, pen: null, new RoundedRect(new Rect(0, y, width, BarHeight), radius));
        (double from, double to) = Span(Value, Minimum, Maximum);
        double x = from * width;
        double w = Math.Max((to - from) * width, to > from ? 2 : 0);
        if (w > 0)
        {
            context.DrawRectangle(fill, pen: null, new RoundedRect(new Rect(x, y, w, BarHeight), radius));
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (string.Equals(change.Property.Name, "ActualThemeVariant", StringComparison.Ordinal))
        {
            InvalidateVisual();
        }
    }

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out object? found) && found is IBrush brush ? brush : fallback;
}
