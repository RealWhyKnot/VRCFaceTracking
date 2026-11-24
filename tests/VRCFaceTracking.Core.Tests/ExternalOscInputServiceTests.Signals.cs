using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Tests;

public sealed partial class ExternalOscInputServiceTests
{
    [Fact]
    public void UnmappedPacketsDoNotCountAsReceiving()
    {
        using ExternalOscInputService service = Create(new ExternalOscInputSettings { Enabled = true, Port = FreePort() });
        Assert.True(service.Apply());
        byte[] unmapped = Message("/avatar/parameters/NotAShape", 1f);

        for (int i = 0; i < 50; i++)
        {
            Assert.False(Ingest(service, unmapped));
        }

        Assert.Equal(ExternalOscState.Listening, service.Status.State);
        Assert.Equal(0, service.Status.PacketsPerSecond);
        Assert.Null(service.SinceLastData);

        Assert.True(Ingest(service, Message("/jawOpen", 0.4f)));
        Assert.Equal(ExternalOscState.Receiving, service.Status.State);
        Assert.NotNull(service.SinceLastData);
    }

    [Fact]
    public void OnlyMappedPacketsSignalNewData()
    {
        using ExternalOscInputService service = Create();

        Ingest(service, Message("/avatar/parameters/NotAShape", 1f));
        Assert.False(service.DataArrived.WaitOne(0));

        Ingest(service, Message("/jawOpen", 0.2f));
        Assert.True(service.DataArrived.WaitOne(0));
        Assert.False(service.DataArrived.WaitOne(0));
    }

    [Fact]
    public void SinceLastDataIsEmptyWhileDisabled()
    {
        using ExternalOscInputService service = Create();

        Ingest(service, Message("/jawOpen", 0.2f));

        Assert.Null(service.SinceLastData);
    }

    [Fact]
    public void VrchatPortChangedOntoTheExternalPortStopsTheListener()
    {
        int port = FreePort();
        var target = new NotifyingTarget();
        using ExternalOscInputService service = new(NullLogger<ExternalOscInputService>.Instance, new ExternalOscInputSettings { Enabled = true, Port = port }, target, new MemorySettings());
        Assert.True(service.Apply());
        Assert.Equal(ExternalOscState.Listening, service.Status.State);

        target.OutPort = port;

        Assert.Equal(ExternalOscState.Error, service.Status.State);
        Assert.Equal(nameof(ExternalOscPortCheck.Conflicting), service.Status.LastError);
        using (var reuse = new UdpClient(new IPEndPoint(IPAddress.Loopback, port)))
        {
            Assert.NotNull(reuse.Client.LocalEndPoint);
        }

        target.OutPort = 9000;

        Assert.Equal(ExternalOscState.Listening, service.Status.State);
        Assert.Throws<SocketException>(() => new UdpClient(new IPEndPoint(IPAddress.Loopback, port)));
    }

    [Fact]
    public async Task PortsLoadedAfterStartAreCheckedToo()
    {
        int port = FreePort();
        var target = new NotifyingTarget();
        using ExternalOscInputService service = new(NullLogger<ExternalOscInputService>.Instance, new ExternalOscInputSettings { Enabled = true, Port = port }, target, new MemorySettings());
        await service.StartAsync(CancellationToken.None);
        Assert.Equal(ExternalOscState.Listening, service.Status.State);

        target.InPort = port;

        Assert.Equal(ExternalOscState.Error, service.Status.State);
        Assert.Equal(nameof(ExternalOscPortCheck.Conflicting), service.Status.LastError);
    }

    [Fact]
    public void UnrelatedVrchatPortChangesKeepTheListener()
    {
        int port = FreePort();
        var target = new NotifyingTarget();
        using ExternalOscInputService service = new(NullLogger<ExternalOscInputService>.Instance, new ExternalOscInputSettings { Enabled = true, Port = port }, target, new MemorySettings());
        Assert.True(service.Apply());
        Assert.True(Ingest(service, Message("/jawOpen", 0.5f)));

        target.InPort = 9101;

        Assert.Equal(ExternalOscState.Receiving, service.Status.State);
        Assert.True(service.TryGetShape(UnifiedExpressions.JawOpen, out _));
    }

    [Fact]
    public void DisposeStopsWatchingVrchatPorts()
    {
        var target = new NotifyingTarget();
        ExternalOscInputService service = new(NullLogger<ExternalOscInputService>.Instance, new ExternalOscInputSettings(), target, new MemorySettings());
        Assert.Equal(1, target.Subscribers);

        service.Dispose();

        Assert.Equal(0, target.Subscribers);
        target.OutPort = 8888;
    }
}
