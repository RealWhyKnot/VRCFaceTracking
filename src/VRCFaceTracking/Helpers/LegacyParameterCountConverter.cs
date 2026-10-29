using System.Globalization;
using Avalonia.Data.Converters;
using VRCFaceTracking.Core.Params;

namespace VRCFaceTracking.Helpers;

internal sealed class LegacyParameterCountConverter : IValueConverter
{
    public static readonly LegacyParameterCountConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is List<Parameter> parameters ? parameters.Count(p => p.Deprecated) : 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
