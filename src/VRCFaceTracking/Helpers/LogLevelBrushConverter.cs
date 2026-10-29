using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Helpers;

public class LogLevelBrushConverter : IValueConverter
{
    public static readonly LogLevelBrushConverter Instance = new();

    private const string DimKey = "TextFillColorTertiaryBrush";
    private const string WarningKey = "SystemFillColorCautionBrush";
    private const string CriticalKey = "SystemFillColorCriticalBrush";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is LogLevel level
            ? level switch
            {
                LogLevel.Trace or LogLevel.Debug => Resolve(DimKey),
                LogLevel.Warning => Resolve(WarningKey),
                LogLevel.Error or LogLevel.Critical => Resolve(CriticalKey),
                _ => AvaloniaProperty.UnsetValue
            }
            : AvaloniaProperty.UnsetValue;

    private static object Resolve(string key)
    {
        var app = Application.Current;
        if (app is not null && app.TryFindResource(key, app.ActualThemeVariant, out var brush) && brush is not null)
        {
            return brush;
        }

        return AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
