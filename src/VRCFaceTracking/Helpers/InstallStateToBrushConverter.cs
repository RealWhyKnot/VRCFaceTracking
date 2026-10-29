using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using VRCFaceTracking.Core.Models;

namespace VRCFaceTracking.Helpers;

internal sealed class InstallStateToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        InstallState.Installed => Resolve("SystemFillColorSuccessBackgroundBrush"),
        InstallState.Outdated => Resolve("SystemFillColorCautionBackgroundBrush"),
        InstallState.AwaitingRestart => Resolve("SystemFillColorAttentionBackgroundBrush"),
        _ => Resolve("SystemFillColorNeutralBackgroundBrush"),
    };

    private static object Resolve(string key)
    {
        Application? app = Application.Current;
        return app is not null && app.TryFindResource(key, app.ActualThemeVariant, out object? brush) && brush is not null
            ? brush
            : AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
