using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using VRCFaceTracking.Core.Models;

namespace VRCFaceTracking.Helpers;

public class InstallStateToBrushConverter : IValueConverter
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
        var app = Application.Current;
        if (app is not null && app.TryFindResource(key, app.ActualThemeVariant, out var brush) && brush is not null)
        {
            return brush;
        }

        return AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class InstallStateToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        InstallState.NotInstalled => Strings.Resources.InstallStateNotInstalled,
        InstallState.Installed => Strings.Resources.InstallStateInstalled,
        InstallState.Outdated => Strings.Resources.InstallStateOutdated,
        InstallState.AwaitingRestart => Strings.Resources.InstallStateAwaitingRestart,
        _ => value?.ToString() ?? string.Empty,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
