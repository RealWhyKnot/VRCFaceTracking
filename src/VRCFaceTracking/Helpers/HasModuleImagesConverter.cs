using System.Globalization;
using Avalonia.Data.Converters;

namespace VRCFaceTracking.Helpers;

internal sealed class HasModuleImagesConverter : IValueConverter
{
    public static readonly HasModuleImagesConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is List<Stream> { Count: > 0 };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
