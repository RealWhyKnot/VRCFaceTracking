using System.Globalization;
using Avalonia.Data.Converters;
using VRCFaceTracking.Core.Params;

namespace VRCFaceTracking.Helpers;

internal sealed class HasLegacyParametersConverter : IValueConverter
{
    public static readonly HasLegacyParametersConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is List<Parameter> parameters && parameters.Any(p => p.Deprecated);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
