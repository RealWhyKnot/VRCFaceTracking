using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace VRCFaceTracking.Helpers;

internal sealed class StreamToBitmapConverter : IValueConverter
{
    public static readonly StreamToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Stream stream)
        {
            return null;
        }

        try
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            return new Bitmap(stream);
        }
        catch (Exception e) when (e is ArgumentException or IOException or ObjectDisposedException)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
