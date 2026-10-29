using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Osc;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Tests;

public sealed class OscSendServiceTests
{
    private sealed class FakeOscTarget : IOscTarget
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsConnected
        {
            get; set;
        }
        public int InPort
        {
            get; set;
        }

        public string? DestinationAddress
        {
            get;
            set
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DestinationAddress)));
            }
        } = "127.0.0.1";

        public int OutPort
        {
            get;
            set
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OutPort)));
            }
        }
    }

    private static OscMessage Float(string address)
    {
        var message = new OscMessage(address, typeof(float))
        {
            Value = 0.5f,
        };
        return message;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(100)]
    public void SendOversizedMessageStillDeliversTheRestOfTheBatch(int oversizedIndex)
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = Assert.IsType<IPEndPoint>(listener.LocalEndPoint).Port;

        var target = new FakeOscTarget();
        var service = new OscSendService(NullLogger<OscSendService>.Instance, target);
        target.OutPort = port;

        int dispatched = 0;
        service.OnMessagesDispatched = count => dispatched = count;

        var batch = new List<OscMessage>();
        for (int i = 0; i < 201; i++)
        {
            batch.Add(i == oversizedIndex
                ? Float("/" + new string('a', 5000))
                : Float($"/avatar/parameters/v2/Shape{i:D3}"));
        }

        service.Send(batch);

        Assert.Equal(batch.Count, dispatched);
    }

    [Fact]
    public void SendAllMessagesFitDispatchesEveryMessage()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = Assert.IsType<IPEndPoint>(listener.LocalEndPoint).Port;

        var target = new FakeOscTarget();
        var service = new OscSendService(NullLogger<OscSendService>.Instance, target);
        target.OutPort = port;

        int dispatched = 0;
        service.OnMessagesDispatched = count => dispatched = count;

        var batch = new List<OscMessage>();
        for (int i = 0; i < 183; i++)
        {
            batch.Add(Float($"/avatar/parameters/v2/Shape{i:D3}"));
        }

        service.Send(batch);

        Assert.Equal(batch.Count, dispatched);
    }

    [Fact]
    public void SendAfterOutPortChangeReachesTheNewTarget()
    {
        using var first = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        first.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        first.ReceiveTimeout = 1000;
        using var second = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        second.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        second.ReceiveTimeout = 1000;

        var target = new FakeOscTarget();
        var service = new OscSendService(NullLogger<OscSendService>.Instance, target);
        var batch = new List<OscMessage> { Float("/avatar/parameters/v2/JawOpen") };
        byte[] buffer = new byte[4096];

        target.OutPort = ((IPEndPoint)first.LocalEndPoint!).Port;
        service.Send(batch);
        Assert.True(first.Receive(buffer) > 0);

        target.OutPort = ((IPEndPoint)second.LocalEndPoint!).Port;
        service.Send(batch);
        Assert.True(second.Receive(buffer) > 0);
        Assert.True(target.IsConnected);
    }

    [Fact]
    public void SendAfterDestinationAddressChangeReachesTheNewAddress()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.ReceiveTimeout = 1000;

        var target = new FakeOscTarget { DestinationAddress = "192.0.2.10" };
        var service = new OscSendService(NullLogger<OscSendService>.Instance, target);
        var batch = new List<OscMessage> { Float("/avatar/parameters/v2/JawOpen") };

        target.OutPort = ((IPEndPoint)listener.LocalEndPoint!).Port;
        target.DestinationAddress = "127.0.0.1";
        service.Send(batch);

        Assert.True(listener.Receive(new byte[4096]) > 0);
    }

    [Fact]
    public void OutPortBeforeAddressIsLoadedWaitsForTheAddress()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.ReceiveTimeout = 1000;

        var target = new FakeOscTarget { DestinationAddress = null };
        var service = new OscSendService(NullLogger<OscSendService>.Instance, target);
        var batch = new List<OscMessage> { Float("/avatar/parameters/v2/JawOpen") };

        target.OutPort = ((IPEndPoint)listener.LocalEndPoint!).Port;
        Assert.Null(target.DestinationAddress);
        Assert.False(target.IsConnected);

        target.DestinationAddress = "127.0.0.1";
        service.Send(batch);

        Assert.True(listener.Receive(new byte[4096]) > 0);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("192.168.1.300")]
    [InlineData("::1")]
    public void DestinationAddressUnusableKeepsThePreviousTarget(string address)
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.ReceiveTimeout = 1000;

        var target = new FakeOscTarget();
        var service = new OscSendService(NullLogger<OscSendService>.Instance, target);
        var batch = new List<OscMessage> { Float("/avatar/parameters/v2/JawOpen") };

        target.OutPort = ((IPEndPoint)listener.LocalEndPoint!).Port;
        target.DestinationAddress = address;
        service.Send(batch);

        Assert.True(listener.Receive(new byte[4096]) > 0);
    }
}
