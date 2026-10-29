using System.Globalization;
using Avalonia.Data.Converters;

namespace VRCFaceTracking.Helpers;

internal sealed class IsLocalTestAvatarConverter : IValueConverter
{
    public static readonly IsLocalTestAvatarConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string id && id.StartsWith("local:", StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
