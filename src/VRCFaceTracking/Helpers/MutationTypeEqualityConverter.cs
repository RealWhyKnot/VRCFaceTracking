using System.Globalization;
using Avalonia.Data.Converters;
using VRCFaceTracking.Core.Params.Data.Mutation;

namespace VRCFaceTracking.Helpers;

internal abstract class MutationTypeEqualityConverter : IValueConverter
{
    protected abstract MutationPropertyType Expected
    {
        get;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is MutationPropertyType t && t == Expected;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
