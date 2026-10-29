using System.Globalization;
using Avalonia.Data.Converters;
using VRCFaceTracking.Core.Models;

namespace VRCFaceTracking.Helpers;

internal sealed class InstallStateToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        InstallState.NotInstalled => Strings.Resources.InstallStateNotInstalled,
        InstallState.Installed => Strings.Resources.InstallStateInstalled,
        InstallState.Outdated => Strings.Resources.InstallStateOutdated,
        InstallState.AwaitingRestart => Strings.Resources.InstallStateAwaitingRestart,
        null => string.Empty,
        _ => value.ToString() ?? string.Empty,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
