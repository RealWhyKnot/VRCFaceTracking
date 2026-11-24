using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace VRCFaceTracking.Controls;

internal sealed class RangeThumb : Control
{
    private static readonly Dictionary<Key, double> StepMultipliers = new()
    {
        [Key.Left] = -1,
        [Key.Down] = -1,
        [Key.Right] = 1,
        [Key.Up] = 1,
        [Key.PageDown] = -10,
        [Key.PageUp] = 10,
    };

    private readonly RangeSlider _owner;

    public RangeThumb(RangeSlider owner, bool isUpper)
    {
        _owner = owner;
        IsUpper = isUpper;
        Focusable = true;
        IsTabStop = true;
    }

    public bool IsUpper { get; }

    public override void Render(DrawingContext context)
    {
        double radius = Math.Min(Bounds.Width, Bounds.Height) / 2;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        IBrush face = _owner.Brush("ControlSolidFillColorDefaultBrush", Brushes.White);
        IBrush edge = _owner.Brush("ControlStrongStrokeColorDefaultBrush", Brushes.Gray);
        IBrush dot = IsEffectivelyEnabled
            ? _owner.Brush("AccentFillColorDefaultBrush", Brushes.DodgerBlue)
            : _owner.Brush("AccentFillColorDisabledBrush", Brushes.Gray);
        context.DrawEllipse(face, new Pen(edge, thickness: 1), center, radius - 0.5, radius - 0.5);
        context.DrawEllipse(dot, pen: null, center, radius * 0.45, radius * 0.45);
        if (IsFocused && PseudoClasses.Contains(":focus-visible"))
        {
            context.DrawEllipse(brush: null, new Pen(_owner.Brush("FocusStrokeColorOuterBrush", Brushes.White), thickness: 2), center, radius + 2, radius + 2);
        }
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        double step = _owner.Step > 0 ? _owner.Step : 0.01;
        double current = IsUpper ? _owner.Upper : _owner.Lower;
        double? target = null;
        if (e.Key == Key.Home)
        {
            target = _owner.Minimum;
        }
        else if (e.Key == Key.End)
        {
            target = _owner.Maximum;
        }
        else if (StepMultipliers.TryGetValue(e.Key, out double multiplier))
        {
            target = current + (step * multiplier);
        }

        if (target is { } value)
        {
            _owner.MoveThumb(IsUpper, value);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
