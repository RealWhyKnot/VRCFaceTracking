using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace VRCFaceTracking.Controls;

internal sealed class RangeSlider : Control
{
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<RangeSlider, double>(nameof(Minimum));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<RangeSlider, double>(nameof(Maximum), 1d);

    public static readonly StyledProperty<double> LowerProperty =
        AvaloniaProperty.Register<RangeSlider, double>(nameof(Lower), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> UpperProperty =
        AvaloniaProperty.Register<RangeSlider, double>(nameof(Upper), 1d, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<RangeSlider, double>(nameof(Step), 0.01d);

    public static readonly StyledProperty<double> MinimumGapProperty =
        AvaloniaProperty.Register<RangeSlider, double>(nameof(MinimumGap), 0.05d);

    public static readonly StyledProperty<string?> LowerNameProperty =
        AvaloniaProperty.Register<RangeSlider, string?>(nameof(LowerName));

    public static readonly StyledProperty<string?> UpperNameProperty =
        AvaloniaProperty.Register<RangeSlider, string?>(nameof(UpperName));

    private const double ThumbSize = 18;
    private const double TrackHeight = 4;
    private const double DesiredHeight = 32;

    private RangeThumb? _dragging;

    static RangeSlider()
    {
        AffectsArrange<RangeSlider>(MinimumProperty, MaximumProperty, LowerProperty, UpperProperty);
        AffectsRender<RangeSlider>(MinimumProperty, MaximumProperty, LowerProperty, UpperProperty);
        FocusableProperty.OverrideDefaultValue<RangeSlider>(defaultValue: false);
    }

    public RangeSlider()
    {
        LowerThumb = new RangeThumb(this, isUpper: false);
        UpperThumb = new RangeThumb(this, isUpper: true);
        LogicalChildren.Add(LowerThumb);
        LogicalChildren.Add(UpperThumb);
        VisualChildren.Add(LowerThumb);
        VisualChildren.Add(UpperThumb);
        Cursor = new Cursor(StandardCursorType.Hand);
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

    public double Lower
    {
        get => GetValue(LowerProperty);
        set => SetValue(LowerProperty, value);
    }

    public double Upper
    {
        get => GetValue(UpperProperty);
        set => SetValue(UpperProperty, value);
    }

    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public double MinimumGap
    {
        get => GetValue(MinimumGapProperty);
        set => SetValue(MinimumGapProperty, value);
    }

    public string? LowerName
    {
        get => GetValue(LowerNameProperty);
        set => SetValue(LowerNameProperty, value);
    }

    public string? UpperName
    {
        get => GetValue(UpperNameProperty);
        set => SetValue(UpperNameProperty, value);
    }

    internal RangeThumb LowerThumb { get; }

    internal RangeThumb UpperThumb { get; }

    public static (double Lower, double Upper) Constrain(double lower, double upper, double minimum, double maximum, double gap, bool movingUpper)
    {
        double span = Math.Max(0, maximum - minimum);
        gap = Math.Clamp(gap, 0, span);
        lower = Math.Clamp(double.IsFinite(lower) ? lower : minimum, minimum, maximum - gap);
        upper = Math.Clamp(double.IsFinite(upper) ? upper : maximum, minimum + gap, maximum);
        if (upper - lower < gap)
        {
            if (movingUpper)
            {
                lower = upper - gap;
            }
            else
            {
                upper = lower + gap;
            }
        }

        return (Math.Round(lower, 6), Math.Round(upper, 6));
    }

    public void MoveThumb(bool upper, double value)
    {
        double snapped = NumberField.Snap(value, Minimum, Maximum, Step);
        double gap = MinimumGap;
        if (upper)
        {
            Upper = Math.Max(snapped, Math.Round(Lower + gap, 6));
            Upper = Math.Min(Upper, Maximum);
        }
        else
        {
            Lower = Math.Min(snapped, Math.Round(Upper - gap, 6));
            Lower = Math.Max(Lower, Minimum);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LowerNameProperty)
        {
            AutomationProperties.SetName(LowerThumb, LowerName);
        }
        else if (change.Property == UpperNameProperty)
        {
            AutomationProperties.SetName(UpperThumb, UpperName);
        }
        else if (change.Property == IsEffectivelyEnabledProperty || string.Equals(change.Property.Name, "ActualThemeVariant", StringComparison.Ordinal))
        {
            InvalidateVisual();
            LowerThumb.InvalidateVisual();
            UpperThumb.InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        LowerThumb.Measure(new Size(ThumbSize, ThumbSize));
        UpperThumb.Measure(new Size(ThumbSize, ThumbSize));
        double width = double.IsInfinity(availableSize.Width) ? 160 : availableSize.Width;
        return new Size(width, DesiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double top = (finalSize.Height - ThumbSize) / 2;
        LowerThumb.Arrange(new Rect(XFor(Lower, finalSize.Width) - (ThumbSize / 2), top, ThumbSize, ThumbSize));
        UpperThumb.Arrange(new Rect(XFor(Upper, finalSize.Width) - (ThumbSize / 2), top, ThumbSize, ThumbSize));
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width;
        double y = (Bounds.Height - TrackHeight) / 2;
        double start = ThumbSize / 2;
        double end = width - (ThumbSize / 2);
        IBrush track = Brush("ControlStrongFillColorDefaultBrush", Brushes.Gray);
        IBrush fill = IsEffectivelyEnabled
            ? Brush("AccentFillColorDefaultBrush", Brushes.DodgerBlue)
            : Brush("AccentFillColorDisabledBrush", Brushes.Gray);
        context.DrawRectangle(track, pen: null, new RoundedRect(new Rect(start, y, Math.Max(0, end - start), TrackHeight), TrackHeight / 2));
        double from = XFor(Lower, width);
        double to = XFor(Upper, width);
        context.DrawRectangle(fill, pen: null, new RoundedRect(new Rect(from, y, Math.Max(0, to - from), TrackHeight), TrackHeight / 2));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEffectivelyEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        double value = ValueFor(e.GetPosition(this).X);
        bool upper = Math.Abs(value - Upper) < Math.Abs(value - Lower) || (Math.Abs(value - Upper) == Math.Abs(value - Lower) && value > Upper);
        _dragging = upper ? UpperThumb : LowerThumb;
        _dragging.Focus(NavigationMethod.Pointer);
        MoveThumb(upper, value);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging != null)
        {
            MoveThumb(_dragging == UpperThumb, ValueFor(e.GetPosition(this).X));
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging != null)
        {
            _dragging = null;
            e.Pointer.Capture(control: null);
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = null;
    }

    internal IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out object? found) && found is IBrush brush ? brush : fallback;

    private double XFor(double value, double width)
    {
        double span = Maximum - Minimum;
        double t = span <= 0 ? 0 : Math.Clamp((value - Minimum) / span, 0, 1);
        return (ThumbSize / 2) + (t * Math.Max(0, width - ThumbSize));
    }

    private double ValueFor(double x)
    {
        double usable = Math.Max(1, Bounds.Width - ThumbSize);
        double t = Math.Clamp((x - (ThumbSize / 2)) / usable, 0, 1);
        return Minimum + (t * (Maximum - Minimum));
    }
}
