using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Helpers;

internal sealed class LogLevelBrushConverter : IValueConverter
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
                LogLevel.Information or LogLevel.None => AvaloniaProperty.UnsetValue,
                _ => AvaloniaProperty.UnsetValue,
            }
            : AvaloniaProperty.UnsetValue;

    private static object Resolve(string key)
    {
        Application? app = Application.Current;
        return app is not null && app.TryFindResource(key, app.ActualThemeVariant, out object? brush) && brush is not null
            ? brush
            : AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
