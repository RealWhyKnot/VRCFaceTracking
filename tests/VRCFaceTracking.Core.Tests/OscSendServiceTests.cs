using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.OSC;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Tests;

public class OscSendServiceTests
{
    private sealed class FakeOscTarget : IOscTarget
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private int _outPort;
        private string _destinationAddress = "127.0.0.1";

        public bool IsConnected
        {
            get; set;
        }
        public int InPort
        {
            get; set;
        }

        public string DestinationAddress
        {
            get => _destinationAddress;
            set
            {
                _destinationAddress = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DestinationAddress)));
            }
        }

        public int OutPort
        {
            get => _outPort;
            set
            {
                _outPort = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OutPort)));
            }
        }
    }

    private static OscMessage Float(string address)
    {
        var message = new OscMessage(address, typeof(float));
        message.Value = 0.5f;
        return message;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(100)]
    public void Send_OversizedMessage_StillDeliversTheRestOfTheBatch(int oversizedIndex)
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)listener.LocalEndPoint).Port;

        var target = new FakeOscTarget();
        var service = new OscSendService(NullLogger<OscSendService>.Instance, target);
        target.OutPort = port;

        var dispatched = 0;
        service.OnMessagesDispatched = count => dispatched = count;

        var batch = new List<OscMessage>();
        for (var i = 0; i < 201; i++)
        {
            batch.Add(i == oversizedIndex
                ? Float("/" + new string('a', 5000))
                : Float($"/avatar/parameters/v2/Shape{i:D3}"));
        }

        service.Send(batch);

        Assert.Equal(batch.Count, dispatched);
    }

    [Fact]
    public void Send_AllMessagesFit_DispatchesEveryMessage()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)listener.LocalEndPoint).Port;

        var target = new FakeOscTarget();
        var service = new OscSendService(NullLogger<OscSendService>.Instance, target);
        target.OutPort = port;

        var dispatched = 0;
        service.OnMessagesDispatched = count => dispatched = count;

        var batch = new List<OscMessage>();
        for (var i = 0; i < 183; i++)
        {
            batch.Add(Float($"/avatar/parameters/v2/Shape{i:D3}"));
        }

        service.Send(batch);

        Assert.Equal(batch.Count, dispatched);
    }

    [Fact]
    public void Send_AfterOutPortChange_ReachesTheNewTarget()
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
        var buffer = new byte[4096];

        target.OutPort = ((IPEndPoint)first.LocalEndPoint!).Port;
        service.Send(batch);
        Assert.True(first.Receive(buffer) > 0);

        target.OutPort = ((IPEndPoint)second.LocalEndPoint!).Port;
        service.Send(batch);
        Assert.True(second.Receive(buffer) > 0);
        Assert.True(target.IsConnected);
    }

    [Fact]
    public void Send_AfterDestinationAddressChange_ReachesTheNewAddress()
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
    public void OutPort_BeforeAddressIsLoaded_WaitsForTheAddress()
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
    public void DestinationAddress_Unusable_KeepsThePreviousTarget(string address)
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
