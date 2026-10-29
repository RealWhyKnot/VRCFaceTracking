using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using VRCFaceTracking.Helpers;

namespace VRCFaceTracking.Tests;

public sealed class StreamToBitmapConverterTests
{
    private static object? Convert(object? value) =>
        StreamToBitmapConverter.Instance.Convert(value, typeof(Bitmap), parameter: null, CultureInfo.InvariantCulture);

    private static MemoryStream ValidPng()
    {
        using var bitmap = new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream;
    }

    [AvaloniaFact]
    public void ValidImageBecomesABitmap()
    {
        using MemoryStream png = ValidPng();

        using Bitmap bitmap = Assert.IsType<Bitmap>(Convert(png));

        Assert.Equal(new PixelSize(4, 4), bitmap.PixelSize);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(100000)]
    public void GarbageImageBecomesNothing(int length)
    {
        byte[] bytes = new byte[length];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i * 31);
        }

        Assert.Null(Convert(new MemoryStream(bytes)));
    }

    [AvaloniaFact]
    public void TruncatedImageBecomesNothing()
    {
        using MemoryStream png = ValidPng();
        byte[] bytes = png.ToArray();

        Assert.Null(Convert(new MemoryStream(bytes[..(bytes.Length / 2)])));
    }

    [AvaloniaFact]
    public void DisposedStreamBecomesNothing()
    {
        MemoryStream png = ValidPng();
        png.Dispose();

        Assert.Null(Convert(png));
    }

    [AvaloniaFact]
    public void BrokenIconStillRendersInAnImage()
    {
        var image = new Image { Width = 32, Height = 32 };
        image.Bind(Image.SourceProperty, new Binding { Converter = StreamToBitmapConverter.Instance, Source = new MemoryStream([1, 2, 3, 4]) });
        var window = new Window { Content = image, Width = 200, Height = 200 };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Null(image.Source);
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
