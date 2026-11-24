using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Types;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Strings;
using VRCFaceTracking.ViewModels;
using VRCFaceTracking.Views;

namespace VRCFaceTracking.Tests;

public sealed class CameraStreamsTests
{
    private static IReadOnlyList<CameraStreamSnapshot> Three() =>
    [
        CameraTestData.Snap("left", CameraPixelFormat.Gray8),
        CameraTestData.Snap("right", CameraPixelFormat.Rgba8888),
        CameraTestData.Snap("mouth", CameraPixelFormat.Bgra8888),
    ];

    private static byte[] ReadPixel(Bitmap bitmap)
    {
        WriteableBitmap writable = Assert.IsType<WriteableBitmap>(bitmap);
        using ILockedFramebuffer buffer = writable.Lock();
        byte[] pixel = new byte[4];
        Marshal.Copy(buffer.Address, pixel, 0, 4);
        return pixel;
    }

    private static byte[] Filled(int pixels, byte a, byte b, byte c, byte d)
    {
        byte[] data = new byte[pixels * 4];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i] = a;
            data[i + 1] = b;
            data[i + 2] = c;
            data[i + 3] = d;
        }

        return data;
    }

    [Fact]
    public void CameraCardIsHiddenWithoutFrames()
    {
        var cameras = new CameraStreamsViewModel();
        cameras.Refresh([]);
        Assert.False(cameras.HasCameras);
        Assert.Empty(cameras.Streams);
    }

    [AvaloniaFact]
    public void ThreeStreamsAppearBlurredByDefault()
    {
        var cameras = new CameraStreamsViewModel();
        cameras.Refresh(Three());

        Assert.True(cameras.HasCameras);
        Assert.Equal(3, cameras.Streams.Count);
        Assert.True(cameras.IsBlurred);
        Assert.Equal(Resources.Camera_Show, cameras.BlurButtonText);
        Assert.All(cameras.Streams, stream =>
        {
            Assert.True(stream.HasFrame);
            Assert.True(stream.ShowCover);
            Assert.NotNull(stream.Source);
        });
    }

    [AvaloniaFact]
    public void BlurToggleRevealsAndHidesEveryStream()
    {
        var cameras = new CameraStreamsViewModel();
        cameras.Refresh(Three());

        cameras.ToggleBlurCommand.Execute(parameter: null);
        Assert.All(cameras.Streams, stream => Assert.False(stream.ShowCover));
        Assert.Equal(Resources.Camera_Blur, cameras.BlurButtonText);

        cameras.ToggleBlurCommand.Execute(parameter: null);
        Assert.All(cameras.Streams, stream => Assert.True(stream.ShowCover));
    }

    [AvaloniaFact]
    public void GuidesReachEveryStream()
    {
        var cameras = new CameraStreamsViewModel();
        cameras.Refresh(Three());
        cameras.ShowGuides = true;
        Assert.All(cameras.Streams, stream => Assert.True(stream.ShowGuides));
    }

    [AvaloniaFact]
    public void RgbaFramesAreSwappedIntoBgra()
    {
        var cameras = new CameraStreamsViewModel();
        cameras.Refresh([CameraTestData.Snap("a", CameraPixelFormat.Rgba8888, 4, 4, data: Filled(16, 255, 10, 20, 255))]);
        Assert.Equal(new byte[] { 20, 10, 255, 255 }, ReadPixel(cameras.Streams[0].Source!));
    }

    [AvaloniaFact]
    public void BgraFramesAreCopiedAsIs()
    {
        var cameras = new CameraStreamsViewModel();
        cameras.Refresh([CameraTestData.Snap("a", CameraPixelFormat.Bgra8888, 4, 4, data: Filled(16, 20, 10, 255, 255))]);
        Assert.Equal(new byte[] { 20, 10, 255, 255 }, ReadPixel(cameras.Streams[0].Source!));
    }

    [AvaloniaFact]
    public void GrayFramesExpandToOpaqueBgra()
    {
        var cameras = new CameraStreamsViewModel();
        cameras.Refresh([CameraTestData.Snap("a", CameraPixelFormat.Gray8, 4, 4, data: [.. Enumerable.Repeat((byte)100, 16)])]);
        Assert.Equal(new byte[] { 100, 100, 100, 255 }, ReadPixel(cameras.Streams[0].Source!));
    }

    [AvaloniaFact]
    public void EncodedFramesDecodeAndBadBytesDoNotThrow()
    {
        using var stream = new MemoryStream();
        using (var source = new WriteableBitmap(new PixelSize(8, 8), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul))
        {
            source.Save(stream);
        }

        var cameras = new CameraStreamsViewModel();
        cameras.Refresh([CameraTestData.Snap("good", CameraPixelFormat.Jpeg, 8, 8, data: stream.ToArray())]);
        Assert.NotNull(cameras.Streams[0].Source);

        cameras.Refresh([CameraTestData.Snap("bad", CameraPixelFormat.Jpeg, 8, 8, data: [1, 2, 3, 4, 5])]);
        Assert.Null(cameras.Streams[0].Source);
    }

    [AvaloniaFact]
    public void ShortFramesAreIgnored()
    {
        var cameras = new CameraStreamsViewModel();
        cameras.Refresh([CameraTestData.Snap("a", CameraPixelFormat.Rgba8888, 64, 64, data: new byte[10])]);
        Assert.False(cameras.Streams.Count > 0 && cameras.Streams[0].HasFrame);
    }

    [AvaloniaFact]
    public void StreamCountIsCappedAtSix()
    {
        var cameras = new CameraStreamsViewModel();
        cameras.Refresh([.. Enumerable.Range(0, 9).Select(i => CameraTestData.Snap("s" + i))]);
        Assert.Equal(CameraStreamRegistry.MaxStreams, cameras.Streams.Count);
    }

    [AvaloniaFact]
    public void FrameWidthFollowsAspectRatio()
    {
        var cameras = new CameraStreamsViewModel();
        cameras.Refresh([CameraTestData.Snap("sq", width: 128, height: 128), CameraTestData.Snap("wide", width: 128, height: 96)]);
        Assert.Equal(180, cameras.Streams[0].FrameWidth);
        Assert.Equal(240, cameras.Streams[1].FrameWidth);
    }

    [AvaloniaFact]
    public void ControlButtonsFollowAdvertisedCapabilities()
    {
        var sent = new List<(Guid Module, CameraControlCommand Command)>();
        var cameras = new CameraStreamsViewModel((module, command) =>
        {
            sent.Add((module, command));
            return true;
        });
        cameras.Refresh(
        [
            CameraTestData.Snap("plain"),
            CameraTestData.Snap("both", capabilities: CameraControlCapabilities.Recenter | CameraControlCapabilities.Recalibrate),
            CameraTestData.Snap("center", capabilities: CameraControlCapabilities.Recenter),
        ]);

        Assert.False(cameras.Streams[0].HasControls);
        Assert.True(cameras.Streams[1].CanRecenter && cameras.Streams[1].CanRecalibrate);
        Assert.True(cameras.Streams[2].CanRecenter);
        Assert.False(cameras.Streams[2].CanRecalibrate);

        cameras.Streams[1].RecenterCommand.Execute(parameter: null);
        cameras.Streams[1].RecalibrateCommand.Execute(parameter: null);

        Assert.Equal(2, sent.Count);
        Assert.Equal(CameraTestData.Module, sent[0].Module);
        Assert.Equal(new CameraControlCommand("both", CameraControlKind.Recenter), sent[0].Command);
        Assert.Equal(CameraControlKind.Recalibrate, sent[1].Command.Kind);
    }

    [AvaloniaFact]
    public void FreezeKeepsTheLastFrame()
    {
        var cameras = new CameraStreamsViewModel();
        cameras.Refresh([CameraTestData.Snap("a", sequence: 1)]);
        Bitmap? before = cameras.Streams[0].Source;

        cameras.IsFrozen = true;
        cameras.Refresh([CameraTestData.Snap("a", sequence: 2)]);

        Assert.Same(before, cameras.Streams[0].Source);
        Assert.Equal(TrackingText.Format(Resources.CameraDetail_Frozen, 64, 64), cameras.Streams[0].Detail);
    }

    [AvaloniaFact]
    public void ResetRestoresPrivacyDefaults()
    {
        var cameras = new CameraStreamsViewModel();
        cameras.Refresh(Three());
        cameras.IsBlurred = false;
        cameras.IsFrozen = true;

        cameras.Reset();

        Assert.True(cameras.IsBlurred);
        Assert.False(cameras.IsFrozen);
        Assert.False(cameras.HasCameras);
        Assert.Empty(cameras.Streams);
    }

    [AvaloniaFact]
    public void StreamsLeaveWhenTheyDisappear()
    {
        var cameras = new CameraStreamsViewModel();
        cameras.Refresh(Three());
        cameras.Refresh([CameraTestData.Snap("right")]);
        Assert.Single(cameras.Streams);
        Assert.Equal("right", cameras.Streams[0].StreamId);
    }

    [AvaloniaFact]
    public void StaleStreamsDropAndStayGoneUntilNewFrames()
    {
        var clock = new ManualClock();
        var cameras = new CameraStreamsViewModel(time: clock);
        cameras.Refresh([CameraTestData.Snap("a", sequence: 5)]);
        Assert.True(cameras.HasCameras);

        clock.Advance(TimeSpan.FromSeconds(6));
        cameras.Refresh([CameraTestData.Snap("a", sequence: 5)]);
        Assert.False(cameras.HasCameras);

        cameras.Refresh([CameraTestData.Snap("a", sequence: 5)]);
        Assert.False(cameras.HasCameras);

        cameras.Refresh([CameraTestData.Snap("a", sequence: 6)]);
        Assert.True(cameras.HasCameras);
    }

    [AvaloniaFact]
    public void DetailShowsLabelSizeAndRate()
    {
        var clock = new ManualClock();
        var cameras = new CameraStreamsViewModel(time: clock);
        for (int i = 1; i <= 12; i++)
        {
            cameras.Refresh([CameraTestData.Snap("left", sequence: i, label: "Left eye")]);
            clock.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal("Left eye", cameras.Streams[0].Title);
        Assert.Contains("64x64", cameras.Streams[0].Detail, StringComparison.Ordinal);
        Assert.Contains("fps", cameras.Streams[0].Detail, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void LegacyStreamsKeepEyeAndLipTitles()
    {
        var cameras = new CameraStreamsViewModel();
        cameras.Refresh([CameraTestData.Snap("eye"), CameraTestData.Snap("lip")]);
        Assert.Equal(Resources.CameraStreamsEye, cameras.Streams[0].Title);
        Assert.Equal(Resources.CameraStreamsLip, cameras.Streams[1].Title);
    }

    [AvaloniaFact]
    public void HomePageRendersAGridAndGatesButtons()
    {
        var page = new MainPage();
        var window = new Window { Content = page, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)page.DataContext!;
        vm.Stop();

        try
        {
            vm.Cameras.Refresh(
            [
                CameraTestData.Snap("left", CameraPixelFormat.Gray8),
                CameraTestData.Snap("right", CameraPixelFormat.Rgba8888, capabilities: CameraControlCapabilities.Recenter),
                CameraTestData.Snap("mouth", CameraPixelFormat.Bgra8888),
            ]);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(3, page.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("camera-frame") && b.IsEffectivelyVisible));
            Button[] recenter = [.. page.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && Equals(b.Content, Resources.Camera_Recenter))];
            Assert.Single(recenter);
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && Equals(b.Content, Resources.Camera_Recalibrate));
        }
        finally
        {
            vm.Cameras.Reset();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void LeavingHomeBlursCamerasAgain()
    {
        var page = new MainPage();
        var window = new Window { Content = page, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)page.DataContext!;

        try
        {
            vm.Start();
            vm.Cameras.Refresh(Three());
            vm.Cameras.IsBlurred = false;

            vm.Stop();

            Assert.True(vm.Cameras.IsBlurred);
            Assert.Empty(vm.Cameras.Streams);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
