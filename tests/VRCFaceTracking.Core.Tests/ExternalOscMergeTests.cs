using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Tests;

public sealed class ExternalOscMergeTests : IDisposable
{
    private readonly ManualClock _clock = new();
    private readonly SignalCapabilityTracker _tracker = new();
    private readonly List<ExternalOscInputService> _services = [];

    public void Dispose()
    {
        foreach (ExternalOscInputService service in _services)
        {
            service.Dispose();
        }
    }

    private sealed class Target : IOscTarget
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged
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

    private sealed class Store : ILocalSettingsService
    {
        public Task<T?> ReadSettingAsync<T>(string key, T? defaultValue, bool forceLocal, CancellationToken cancellationToken) => Task.FromResult(defaultValue);

        public Task SaveSettingAsync<T>(string key, T value, bool forceLocal, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task LoadAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private ExternalOscInputService Create(bool enabled, SignalCapabilityTracker? tracker = null)
    {
        int port;
        using (var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            port = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        }

        var settings = new ExternalOscInputSettings { Enabled = enabled, Port = port };
        var service = new ExternalOscInputService(NullLogger<ExternalOscInputService>.Instance, settings, new Target(), new Store(), _clock, tracker ?? _tracker);
        _services.Add(service);
        Assert.True(service.Apply());
        return service;
    }

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

    private static void AddString(List<byte> bytes, string text)
    {
        bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(text));
        bytes.Add(0);
        while (bytes.Count % 4 != 0)
        {
            bytes.Add(0);
        }
    }

    private static void Send(ExternalOscInputService service, string address, float value)
    {
        byte[] datagram = Message(address, value);
        service.Ingest(datagram, datagram.Length);
    }

    private static float Weight(UnifiedTrackingData data, UnifiedExpressions shape) => data.Shapes[(int)shape].Weight;

    private static void ModuleDrives(SignalCapabilityTracker tracker, params UnifiedExpressions[] shapes)
    {
        var data = new UnifiedTrackingData();
        foreach (UnifiedExpressions shape in shapes)
        {
            data.Shapes[(int)shape].Weight = 0.5f;
        }

        tracker.Observe(data, 1);
        tracker.Observe(data, 1 + Stopwatch.Frequency);
    }

    [Fact]
    public void EnabledAndFreshValueIsApplied()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/jawOpen", 0.6f);
        var data = new UnifiedTrackingData();

        service.Merge(data);

        Assert.Equal(0.6f, Weight(data, UnifiedExpressions.JawOpen));
        Assert.Equal(0f, Weight(data, UnifiedExpressions.JawForward));
        Assert.True(_tracker.Snapshot.IsLive(UnifiedExpressions.JawOpen));
        Assert.False(_tracker.Snapshot.IsModuleLive(UnifiedExpressions.JawOpen));
    }

    [Fact]
    public void DisabledAppliesNothing()
    {
        ExternalOscInputService service = Create(enabled: false);
        Send(service, "/jawOpen", 0.6f);
        var data = new UnifiedTrackingData();

        service.Merge(data);

        Assert.Equal(0f, Weight(data, UnifiedExpressions.JawOpen));
        Assert.False(_tracker.Snapshot.IsLive(UnifiedExpressions.JawOpen));
    }

    [Fact]
    public void StaleValueStopsAndReleasesOnce()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/jawOpen", 0.6f);
        service.Merge(new UnifiedTrackingData());
        long versionLive = _tracker.Snapshot.Version;

        _clock.Advance(TimeSpan.FromMilliseconds(1500));
        var data = new UnifiedTrackingData();
        service.Merge(data);
        long versionReleased = _tracker.Snapshot.Version;
        service.Merge(new UnifiedTrackingData());
        service.Merge(new UnifiedTrackingData());

        Assert.Equal(0f, Weight(data, UnifiedExpressions.JawOpen));
        Assert.False(_tracker.Snapshot.IsLive(UnifiedExpressions.JawOpen));
        Assert.True(versionReleased > versionLive);
        Assert.Equal(versionReleased, _tracker.Snapshot.Version);
    }

    [Fact]
    public void ValueRefreshedInsideTheWindowStaysApplied()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/jawOpen", 0.6f);
        _clock.Advance(TimeSpan.FromMilliseconds(800));
        Send(service, "/jawOpen", 0.7f);
        _clock.Advance(TimeSpan.FromMilliseconds(800));
        var data = new UnifiedTrackingData();

        service.Merge(data);

        Assert.Equal(0.7f, Weight(data, UnifiedExpressions.JawOpen));
    }

    [Fact]
    public void ModuleDrivenShapeIsLeftAlone()
    {
        ModuleDrives(_tracker, UnifiedExpressions.JawOpen);
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/jawOpen", 0.9f);
        Send(service, "/cheekPuffLeft", 0.4f);
        var data = new UnifiedTrackingData();
        data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = 0.2f;

        service.Merge(data);

        Assert.Equal(0.2f, Weight(data, UnifiedExpressions.JawOpen));
        Assert.Equal(0.4f, Weight(data, UnifiedExpressions.CheekPuffLeft));
        Assert.True(_tracker.Snapshot.IsModuleLive(UnifiedExpressions.JawOpen));
    }

    [Fact]
    public void ShapeWithNonZeroModuleWeightIsLeftAlone()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/jawOpen", 0.9f);
        var data = new UnifiedTrackingData();
        data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = 0.3f;

        service.Merge(data);

        Assert.Equal(0.3f, Weight(data, UnifiedExpressions.JawOpen));
    }

    private static float FilledCheek(SignalCapabilityTracker tracker, ExternalOscInputService? external)
    {
        var data = new UnifiedTrackingData();
        data.Shapes[(int)UnifiedExpressions.EyeSquintLeft].Weight = 0.8f;
        data.Shapes[(int)UnifiedExpressions.EyeSquintRight].Weight = 0.8f;
        external?.Merge(data);
        var fill = new SignalFillIn
        {
            IsActive = true,
            CurrentAvatar = () => new AvatarParameterSnapshot(usesNativeFaceTracking: false, ["v2/CheekSquintLeft", "v2/CheekSquintRight"]),
            Capabilities = () => tracker.Snapshot,
        };
        fill.MutateData(ref data);
        return Weight(data, UnifiedExpressions.CheekSquintLeft);
    }

    [Fact]
    public void FillInDoesNotDoubleUpOnExternalShape()
    {
        UnifiedExpressions[] eyeSquint = [UnifiedExpressions.EyeSquintLeft, UnifiedExpressions.EyeSquintRight];
        var control = new SignalCapabilityTracker();
        ModuleDrives(control, eyeSquint);
        ModuleDrives(_tracker, eyeSquint);
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/cheekSquintLeft", 0.1f);
        Send(service, "/cheekSquintRight", 0.1f);

        float synthesized = FilledCheek(control, external: null);
        float withExternal = FilledCheek(_tracker, service);

        Assert.Equal(0.4f, synthesized, 3);
        Assert.Equal(0.1f, withExternal, 3);
    }

    [Fact]
    public void GapReportCountsExternalShapeAsCovered()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/jawOpen", 0.5f);
        service.Merge(new UnifiedTrackingData());
        var support = new AvatarSupport(UsesNativeFaceTracking: false, new HashSet<UnifiedExpressions> { UnifiedExpressions.JawOpen });

        var report = AvatarGapReport.Build(_tracker.Snapshot, support, []);

        Assert.Equal(1, report.CoveredCount);
        Assert.Equal(0, report.MissingCount);
    }

    [Fact]
    public void EtvrStyleEyeNamesDriveGazeWithoutScaling()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/LeftEyeX", -0.4f);
        Send(service, "/avatar/parameters/RightEyeX", 0.3f);
        Send(service, "/avatar/parameters/EyesY", 0.2f);
        Send(service, "/avatar/parameters/EyeRightY", -0.6f);
        var data = new UnifiedTrackingData();

        service.Merge(data);

        Assert.Equal(-0.4f, data.Eye.Left.Gaze.x);
        Assert.Equal(0.2f, data.Eye.Left.Gaze.y);
        Assert.Equal(0.3f, data.Eye.Right.Gaze.x);
        Assert.Equal(-0.6f, data.Eye.Right.Gaze.y);
        Assert.Equal(1f, data.Eye.Left.Openness);
    }

    [Fact]
    public void OutOfRangeInputIsClamped()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/jawOpen", 7f);
        Send(service, "/mouthClose", -3f);
        Send(service, "/avatar/parameters/LeftEyeX", -9f);
        Send(service, "/avatar/parameters/LeftEyeLid", 4f);
        var data = new UnifiedTrackingData();

        service.Merge(data);

        Assert.Equal(1f, Weight(data, UnifiedExpressions.JawOpen));
        Assert.Equal(0f, Weight(data, UnifiedExpressions.MouthClosed));
        Assert.Equal(-1f, data.Eye.Left.Gaze.x);
        Assert.Equal(1f, data.Eye.Left.Openness);
    }

    [Fact]
    public void UnknownPathsAreIgnored()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/NotAShape", 1f);
        Send(service, "/somewhere/else", 1f);
        var data = new UnifiedTrackingData();

        service.Merge(data);

        Assert.All(data.Shapes, shape => Assert.Equal(0f, shape.Weight));
        Assert.Equal(0, _tracker.Snapshot.LiveShapeCount);
    }

    [Fact]
    public void ExternalEyeCountsAsLiveUntilStale()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/avatar/parameters/EyeRightY", 0.25f);
        var data = new UnifiedTrackingData();

        service.Merge(data);

        Assert.Equal(0.25f, data.Eye.Right.Gaze.y);
        Assert.Equal(0f, data.Eye.Left.Gaze.y);
        Assert.True(_tracker.Snapshot.EyeRightLive);
        Assert.False(_tracker.Snapshot.ModuleEyeRightLive);

        _clock.Advance(TimeSpan.FromSeconds(2));
        service.Merge(new UnifiedTrackingData());
        Assert.False(_tracker.Snapshot.EyeRightLive);
    }

    [Fact]
    public void TurningTheSettingOffReleasesEverything()
    {
        ExternalOscInputService service = Create(enabled: true);
        Send(service, "/jawOpen", 0.6f);
        service.Merge(new UnifiedTrackingData());
        Assert.True(_tracker.Snapshot.IsLive(UnifiedExpressions.JawOpen));

        service.Settings.Enabled = false;
        service.Apply();
        var data = new UnifiedTrackingData();
        service.Merge(data);

        Assert.Equal(0f, Weight(data, UnifiedExpressions.JawOpen));
        Assert.False(_tracker.Snapshot.IsLive(UnifiedExpressions.JawOpen));
    }
}
