using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Types;
using VRCFaceTracking.ViewModels;
using VRCFaceTracking.Views;

namespace VRCFaceTracking.Tests;

public sealed class CameraBlurTests
{
    private const int Ring = 6;

    private sealed class Frame
    {
        private readonly byte[] _pixels;
        private readonly int _stride;

        public Frame(WriteableBitmap bitmap)
        {
            using ILockedFramebuffer buffer = bitmap.Lock();
            Width = buffer.Size.Width;
            Height = buffer.Size.Height;
            _stride = buffer.RowBytes;
            _pixels = new byte[_stride * Height];
            Marshal.Copy(buffer.Address, _pixels, 0, _pixels.Length);
        }

        public int Width { get; }

        public int Height { get; }

        public int Luma(int x, int y)
        {
            int at = (y * _stride) + (x * 4);
            return (_pixels[at] + _pixels[at + 1] + _pixels[at + 2]) / 3;
        }

        public uint Pixel(int x, int y) => BitConverter.ToUInt32(_pixels, (y * _stride) + (x * 4));
    }

    private static CameraStreamSnapshot SplitFrame(string id, int width, int height)
    {
        byte[] data = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            data.AsSpan(y * width, width / 2).Fill(255);
        }

        return CameraTestData.Snap(id, CameraPixelFormat.Gray8, width, height, data: data);
    }

    private static Rect BoundsIn(Visual visual, Visual root)
    {
        Point origin = visual.TranslatePoint(default, root) ?? throw new InvalidOperationException("visual isn't in the window");
        return new Rect(origin, visual.Bounds.Size);
    }

    private static int EdgeWidth(Frame frame, Rect view)
    {
        int y = (int)(view.Y + (view.Height / 5));
        int from = (int)(view.X + (view.Width * 0.22));
        int to = (int)(view.X + (view.Width * 0.78));
        int bright = 0;
        int dark = int.MaxValue;
        for (int x = from; x < to; x++)
        {
            bright = Math.Max(bright, frame.Luma(x, y));
            dark = Math.Min(dark, frame.Luma(x, y));
        }

        int low = dark + ((bright - dark) / 10);
        int high = bright - ((bright - dark) / 10);
        int width = 0;
        for (int x = from; x < to; x++)
        {
            int luma = frame.Luma(x, y);
            if (luma > low && luma < high)
            {
                width++;
            }
        }

        return width;
    }

    private static List<(int X, int Y)> RingAround(Rect border, Frame frame)
    {
        List<(int X, int Y)> points = [];
        int left = (int)Math.Floor(border.X) - Ring;
        int top = (int)Math.Floor(border.Y) - Ring;
        int right = (int)Math.Ceiling(border.Right) + Ring;
        int bottom = (int)Math.Ceiling(border.Bottom) + Ring;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                bool outside = x < border.X - 1 || x > border.Right || y < border.Y - 1 || y > border.Bottom;
                if (outside && x >= 0 && y >= 0 && x < frame.Width && y < frame.Height)
                {
                    points.Add((x, y));
                }
            }
        }

        return points;
    }

    private static Frame Capture(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        using WriteableBitmap shot = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame was rendered");
        return new Frame(shot);
    }

    [AvaloniaFact]
    public void BlurStaysTheSameOnScreenWhateverTheCameraResolution()
    {
        var page = new MainPage();
        var window = new Window { Content = page, Width = 1280, Height = 1600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)page.DataContext!;
        vm.Stop();

        try
        {
            vm.Cameras.Refresh([SplitFrame("low", 128, 64), SplitFrame("high", 2048, 1024)]);
            Dispatcher.UIThread.RunJobs();
            Border[] borders = [.. page.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("camera-frame"))];
            Viewbox[] views = [.. borders.Select(b => b.GetVisualDescendants().OfType<Viewbox>().Single())];
            Avalonia.Controls.Image[] images = [.. page.GetVisualDescendants().OfType<Avalonia.Controls.Image>().Where(i => i.Classes.Contains("camera-image"))];
            Assert.Equal(2, views.Length);
            Assert.Equal(2, images.Length);
            Assert.All(images, image => Assert.Contains("blurred", image.Classes, StringComparer.Ordinal));
            Assert.All(images, image => Assert.NotNull(image.Effect));

            Frame blurred = Capture(window);
            Rect low = BoundsIn(views[0], window);
            Rect high = BoundsIn(views[1], window);
            int lowEdge = EdgeWidth(blurred, low);
            int highEdge = EdgeWidth(blurred, high);

            vm.Cameras.IsBlurred = false;
            Frame sharp = Capture(window);
            Assert.All(images, image => Assert.Null(image.Effect));

            Assert.InRange(EdgeWidth(sharp, low), 0, 6);
            Assert.InRange(EdgeWidth(sharp, high), 0, 6);
            Assert.InRange(lowEdge, 10, 80);
            Assert.InRange(highEdge, 10, 80);
            Assert.InRange(Math.Abs(lowEdge - highEdge), 0, Math.Max(lowEdge, highEdge) / 4);
            foreach (Border border in borders)
            {
                Assert.All(RingAround(BoundsIn(border, window), blurred), point => Assert.Equal(sharp.Pixel(point.X, point.Y), blurred.Pixel(point.X, point.Y)));
            }
        }
        finally
        {
            vm.Cameras.Reset();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
