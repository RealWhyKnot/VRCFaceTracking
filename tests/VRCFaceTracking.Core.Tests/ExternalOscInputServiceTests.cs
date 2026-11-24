using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Tests;

public sealed class ExternalOscInputServiceTests
{
    private sealed class TestTarget : IOscTarget
    {
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add
            {
            }
            remove
            {
            }
        }

        public bool IsConnected { get; set; }

        public int InPort { get; set; } = 9001;

        public int OutPort { get; set; } = 9000;

        public string? DestinationAddress { get; set; } = "127.0.0.1";
    }

    private sealed class MemorySettings : ILocalSettingsService
    {
        public int Saves { get; private set; }

        public Task<T?> ReadSettingAsync<T>(string key, T? defaultValue, bool forceLocal, CancellationToken cancellationToken) => Task.FromResult(defaultValue);

        public Task SaveSettingAsync<T>(string key, T value, bool forceLocal, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(object target, CancellationToken cancellationToken)
        {
            Saves++;
            return Task.CompletedTask;
        }

        public Task LoadAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static int FreePort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    private static ExternalOscInputService Create(ExternalOscInputSettings? settings = null, TestTarget? target = null, MemorySettings? store = null, TimeProvider? time = null) =>
        new(NullLogger<ExternalOscInputService>.Instance, settings ?? new ExternalOscInputSettings(), target ?? new TestTarget(), store ?? new MemorySettings(), time);

    private static byte[] Message(string address, float value)
    {
        List<byte> bytes = [];
        AddString(bytes, address);
        AddString(bytes, ",f");
        byte[] number = BitConverter.GetBytes(value);
        Array.Reverse(number);
        bytes.AddRange(number);
        return [.. bytes];
    }

    private static byte[] TextMessage(string address, string value)
    {
        List<byte> bytes = [];
        AddString(bytes, address);
        AddString(bytes, ",s");
        AddString(bytes, value);
        return [.. bytes];
    }

    private static void AddString(List<byte> bytes, string text)
    {
        bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(text));
        bytes.Add(0);
        while (bytes.Count % 4 != 0)
        {
            bytes.Add(0);
        }
    }

    private static byte[] Bundle(params byte[][] messages)
    {
        List<byte> bytes = [];
        AddString(bytes, "#bundle");
        bytes.AddRange(new byte[8]);
        foreach (byte[] message in messages)
        {
            byte[] size = BitConverter.GetBytes(message.Length);
            Array.Reverse(size);
            bytes.AddRange(size);
            bytes.AddRange(message);
        }

        return [.. bytes];
    }

    private static bool Ingest(ExternalOscInputService service, byte[] datagram) => service.Ingest(datagram, datagram.Length);

    [Fact]
    public async Task DefaultsToOffAndBindsNothing()
    {
        var defaults = new ExternalOscInputSettings();
        Assert.False(defaults.Enabled);
        Assert.Equal(8888, defaults.Port);

        int port = FreePort();
        using ExternalOscInputService service = Create(new ExternalOscInputSettings { Port = port });
        await service.StartAsync(CancellationToken.None);

        Assert.Equal(ExternalOscState.Disabled, service.Status.State);
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        Assert.NotNull(probe.Client.LocalEndPoint);
    }

    [Fact]
    public void AppliesDisabledWithoutTouchingTheSocketLayer()
    {
        using ExternalOscInputService service = Create();

        Assert.True(service.Apply());
        Assert.Equal(ExternalOscState.Disabled, service.Status.State);
        Assert.Null(service.Status.LastError);
    }

    [Fact]
    public void DecodesVrcftParameterPaths()
    {
        using ExternalOscInputService service = Create();

        Assert.True(Ingest(service, Message("/avatar/parameters/CheekPuffLeft", 0.75f)));
        Assert.True(Ingest(service, Message("/AVATAR/PARAMETERS/jawopen", 0.5f)));

        Assert.True(service.TryGetShape(UnifiedExpressions.CheekPuffLeft, out float puff));
        Assert.Equal(0.75f, puff);
        Assert.True(service.TryGetShape(UnifiedExpressions.JawOpen, out float jaw));
        Assert.Equal(0.5f, jaw);
        Assert.False(service.TryGetShape(UnifiedExpressions.TongueOut, out _));
    }

    [Fact]
    public void DecodesDirectBabblePaths()
    {
        using ExternalOscInputService service = Create();

        Assert.True(Ingest(service, Message("/cheekPuffLeft", 0.4f)));
        Assert.True(Ingest(service, Message("/jawOpen", 0.9f)));
        Assert.True(Ingest(service, Message("/mouthFunnel", 0.6f)));

        Assert.True(service.TryGetShape(UnifiedExpressions.CheekPuffLeft, out float puff));
        Assert.Equal(0.4f, puff);
        Assert.True(service.TryGetShape(UnifiedExpressions.JawOpen, out float jaw));
        Assert.Equal(0.9f, jaw);
        IReadOnlyDictionary<UnifiedExpressions, float> shapes = service.ShapeSnapshot();
        Assert.Equal(0.6f, shapes[UnifiedExpressions.LipFunnelUpperLeft]);
        Assert.Equal(0.6f, shapes[UnifiedExpressions.LipFunnelLowerRight]);
        Assert.False(shapes.ContainsKey(UnifiedExpressions.JawForward));
    }

    [Fact]
    public void DecodesEyePathsAndClampsValues()
    {
        using ExternalOscInputService service = Create();

        Assert.True(Ingest(service, Message("/avatar/parameters/LeftEyeX", -0.25f)));
        Assert.True(Ingest(service, Message("/avatar/parameters/v2/EyeRightY", 3f)));
        Assert.True(Ingest(service, Message("/EyesY", 0.5f)));
        Assert.True(Ingest(service, Message("/avatar/parameters/RightEyeLid", 0.6f)));
        Assert.True(Ingest(service, Message("/jawOpen", 5f)));

        IReadOnlyDictionary<ExternalOscEyeChannel, float> eye = service.EyeSnapshot();
        Assert.Equal(-0.25f, eye[ExternalOscEyeChannel.LeftX]);
        Assert.Equal(1f, eye[ExternalOscEyeChannel.RightY]);
        Assert.Equal(0.5f, eye[ExternalOscEyeChannel.CombinedY]);
        Assert.Equal(0.6f, eye[ExternalOscEyeChannel.RightOpenness]);
        Assert.True(service.TryGetShape(UnifiedExpressions.JawOpen, out float jaw));
        Assert.Equal(1f, jaw);
    }

    [Fact]
    public void UnknownNamesNonFloatsAndGarbageAreIgnored()
    {
        using ExternalOscInputService service = Create();

        Assert.False(Ingest(service, Message("/avatar/parameters/NotAShape", 1f)));
        Assert.False(Ingest(service, Message("/jawOpen", float.NaN)));
        Assert.False(Ingest(service, TextMessage("/jawOpen", "1")));
        Assert.False(Ingest(service, [1, 2, 3]));
        Assert.False(Ingest(service, []));
        Assert.Empty(service.ShapeSnapshot());
        Assert.Empty(service.EyeSnapshot());
    }

    [Fact]
    public void BundlesAreUnpacked()
    {
        using ExternalOscInputService service = Create();

        Assert.True(Ingest(service, Bundle(Message("/jawOpen", 0.3f), Message("/mouthClose", 0.2f))));

        Assert.True(service.TryGetShape(UnifiedExpressions.JawOpen, out float jaw));
        Assert.Equal(0.3f, jaw);
        Assert.True(service.TryGetShape(UnifiedExpressions.MouthClosed, out float closed));
        Assert.Equal(0.2f, closed);
    }

    [Fact]
    public void NestedBundlesStopAtTheDepthLimit()
    {
        using ExternalOscInputService service = Create();
        byte[] nested = Message("/jawOpen", 0.3f);
        for (int i = 0; i < 20; i++)
        {
            nested = Bundle(nested);
        }

        Assert.False(Ingest(service, nested));
    }

    [Theory]
    [InlineData("/avatar/parameters/LeftEyeX", ExternalOscEyeChannel.LeftX)]
    [InlineData("/avatar/parameters/EyeLeftX", ExternalOscEyeChannel.LeftX)]
    [InlineData("/avatar/parameters/v2/EyeLeftY", ExternalOscEyeChannel.LeftY)]
    [InlineData("/avatar/parameters/RightEyeX", ExternalOscEyeChannel.RightX)]
    [InlineData("/avatar/parameters/FT/v2/EyeRightX", ExternalOscEyeChannel.RightX)]
    [InlineData("/avatar/parameters/LeftEyeLid", ExternalOscEyeChannel.LeftOpenness)]
    [InlineData("/RightEyeLid", ExternalOscEyeChannel.RightOpenness)]
    [InlineData("/avatar/parameters/EyesY", ExternalOscEyeChannel.CombinedY)]
    [InlineData("/avatar/parameters/EyeX", ExternalOscEyeChannel.CombinedX)]
    public void RealSenderEyeNamesResolveToChannels(string address, ExternalOscEyeChannel expected)
    {
        ExternalOscTarget? target = ExternalOscParameterMap.Resolve(address);

        Assert.NotNull(target);
        Assert.Equal(expected, target.EyeChannel);
        Assert.Empty(target.Shapes);
    }

    [Theory]
    [InlineData("/eye/LeftX")]
    [InlineData("/avatar/parameters/EyeLidLeft")]
    [InlineData("/avatar/parameters/LeftEyeLidExpandedSqueeze")]
    public void UnconfirmedEyeNamesAreIgnored(string address) => Assert.Null(ExternalOscParameterMap.Resolve(address));

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void OutOfRangePortsAreRejected(int port)
    {
        Assert.Equal(ExternalOscPortCheck.OutOfRange, ExternalOscInputSettings.Check(port, new TestTarget()));

        using ExternalOscInputService service = Create(new ExternalOscInputSettings { Enabled = true, Port = port });
        Assert.False(service.Apply());
        Assert.Equal(ExternalOscState.Error, service.Status.State);
        Assert.NotNull(service.Status.LastError);
    }

    [Fact]
    public void PortsUsedByVrchatConflict()
    {
        var target = new TestTarget { InPort = 9001, OutPort = 9000 };

        Assert.Equal(ExternalOscPortCheck.Conflicting, ExternalOscInputSettings.Check(9001, target));
        Assert.Equal(ExternalOscPortCheck.Conflicting, ExternalOscInputSettings.Check(9000, target));
        Assert.Equal(ExternalOscPortCheck.Valid, ExternalOscInputSettings.Check(8888, target));

        using ExternalOscInputService service = Create(new ExternalOscInputSettings { Enabled = true, Port = 9001 }, target);
        Assert.False(service.Apply());
        Assert.Equal(ExternalOscState.Error, service.Status.State);
    }

    [Fact]
    public void PortAlreadyTakenReportsAnErrorInsteadOfThrowing()
    {
        int port = FreePort();
        using var holder = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        using ExternalOscInputService service = Create(new ExternalOscInputSettings { Enabled = true, Port = port });

        Assert.False(service.Apply());
        Assert.Equal(ExternalOscState.Error, service.Status.State);
        Assert.False(string.IsNullOrEmpty(service.Status.LastError));
    }

    [Fact]
    public async Task ReceivesRealDatagramsOnItsOwnSocket()
    {
        int port = FreePort();
        using ExternalOscInputService service = Create(new ExternalOscInputSettings { Enabled = true, Port = port });

        Assert.True(service.Apply());
        Assert.Equal(ExternalOscState.Listening, service.Status.State);

        using var sender = new UdpClient();
        byte[] datagram = Message("/jawOpen", 0.8f);
        for (int i = 0; i < 100 && !service.TryGetShape(UnifiedExpressions.JawOpen, out _); i++)
        {
            await sender.SendAsync(datagram, datagram.Length, new IPEndPoint(IPAddress.Loopback, port));
            await Task.Delay(20);
        }

        Assert.True(service.TryGetShape(UnifiedExpressions.JawOpen, out float jaw));
        Assert.Equal(0.8f, jaw);
        Assert.Equal(ExternalOscState.Receiving, service.Status.State);
    }

    [Fact]
    public void DisablingStopsListeningAndReleasesThePort()
    {
        int port = FreePort();
        var settings = new ExternalOscInputSettings { Enabled = true, Port = port };
        using ExternalOscInputService service = Create(settings);
        Assert.True(service.Apply());
        Assert.Throws<SocketException>(() => new UdpClient(new IPEndPoint(IPAddress.Loopback, port)));

        settings.Enabled = false;
        Assert.True(service.Apply());

        Assert.Equal(ExternalOscState.Disabled, service.Status.State);
        using var reuse = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        Assert.Empty(service.ShapeSnapshot());
    }

    [Fact]
    public void DisposeStopsCleanlyAndReleasesThePort()
    {
        int port = FreePort();
        ExternalOscInputService service = Create(new ExternalOscInputSettings { Enabled = true, Port = port });
        Assert.True(service.Apply());

        service.Dispose();
        service.Dispose();

        using var reuse = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        Assert.Equal(ExternalOscState.Disabled, service.Status.State);
    }

    [Fact]
    public void ChangingThePortRebindsAndDropsOldValues()
    {
        int first = FreePort();
        var settings = new ExternalOscInputSettings { Enabled = true, Port = first };
        using ExternalOscInputService service = Create(settings);
        Assert.True(service.Apply());
        Ingest(service, Message("/jawOpen", 0.5f));

        int second = FreePort();
        settings.Port = second;
        Assert.True(service.Apply());

        Assert.Empty(service.ShapeSnapshot());
        using var reuse = new UdpClient(new IPEndPoint(IPAddress.Loopback, first));
        Assert.Throws<SocketException>(() => new UdpClient(new IPEndPoint(IPAddress.Loopback, second)));
    }

    [Fact]
    public async Task SettingsAreSavedOnRequest()
    {
        var store = new MemorySettings();
        using ExternalOscInputService service = Create(store: store);

        await service.SaveAsync(CancellationToken.None);

        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public void VersionAdvancesOnlyWhenValuesChange()
    {
        using ExternalOscInputService service = Create();
        int start = service.Version;

        Ingest(service, Message("/avatar/parameters/NotAShape", 1f));
        Assert.Equal(start, service.Version);

        Ingest(service, Message("/jawOpen", 0.1f));
        Assert.True(service.Version > start);
    }
}
