using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace VRCFaceTracking.Controls;

internal sealed class NumberField : TextBox
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<NumberField, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<NumberField, double>(nameof(Minimum));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<NumberField, double>(nameof(Maximum), 1d);

    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<NumberField, double>(nameof(Step), 0.01d);

    public NumberField()
    {
        Text = Format(Value);
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

    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    public static int DecimalsFor(double step) => step >= 1 ? 0 : step >= 0.1 ? 1 : 2;

    public static double Snap(double value, double minimum, double maximum, double step)
    {
        if (!double.IsFinite(value))
        {
            return minimum;
        }

        double clamped = Math.Clamp(value, minimum, maximum);
        if (step <= 0)
        {
            return clamped;
        }

        double snapped = minimum + (Math.Round((clamped - minimum) / step) * step);
        return Math.Round(Math.Clamp(snapped, minimum, maximum), 6);
    }

    public static bool TryParse(string? text, out double value)
    {
        string cleaned = (text ?? string.Empty).Trim().Replace(',', '.').Replace('−', '-');
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    public void Commit()
    {
        if (TryParse(Text, out double typed))
        {
            Value = Snap(typed, Minimum, Maximum, Step);
        }

        Text = Format(Value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == ValueProperty || change.Property == StepProperty) && !IsFocused)
        {
            Text = Format(Value);
        }
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        Commit();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Commit();
        }
        else if (e.Key == Key.Escape)
        {
            Text = Format(Value);
        }
        else if (e.Key is Key.Up or Key.Down)
        {
            double step = Step > 0 ? Step : 0.01;
            double current = TryParse(Text, out double typed) ? typed : Value;
            Value = Snap(current + (e.Key == Key.Up ? step : -step), Minimum, Maximum, Step);
            Text = Format(Value);
        }
        else
        {
            base.OnKeyDown(e);
            return;
        }

        SelectAll();
        e.Handled = true;
    }

    private string Format(double value) => value.ToString("F" + DecimalsFor(Step).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
