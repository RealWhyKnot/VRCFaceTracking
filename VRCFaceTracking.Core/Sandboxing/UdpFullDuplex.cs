using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Sandboxing.IPC;

namespace VRCFaceTracking.Core.Sandboxing;


public class UdpFullDuplex : IDisposable
{
    const int SIO_UDP_CONNRESET = -1744830452;
    const int ETHERNET_FRAME_SIZE = 1500;
    const int TIMEOUT_MILLISECONDS = 10000;

    public int Port
    {
        get; protected set;
    }

    private readonly object _callbackLock;

    protected UdpClient _receivingUdpClient;
    private IPEndPoint _remoteIpEndPoint;
    private readonly ManualResetEvent _closingEvent;
    protected bool _isConnected = false;
    protected SimpleEventBus _eventBus;
    private readonly Task _receiveThread;
    private readonly int _maximumTransferUnit = ETHERNET_FRAME_SIZE;
    private readonly CancellationTokenSource _cts = new();
    public int MTU => _maximumTransferUnit;

    public UdpFullDuplex(int port, int[] reservedPorts = null, IPEndPoint remoteIpEndPoint = null)
    {
        Port = port;
        _closingEvent = new ManualResetEvent(false);
        _callbackLock = new object();
        _eventBus = new SimpleEventBus();

        // try to open the port 10 times, else fail
        for (var i = 0; i < 10; i++)
        {
            try
            {
                _receivingUdpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                // Disable crash from ICMP messages from modules which crashed. Windows only
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    _receivingUdpClient.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
                }
                _receivingUdpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                // Blacklist any reserved ports
                var isReservedPort = false;
                if (reservedPorts != null)
                {
                    var boundPort = ((IPEndPoint)_receivingUdpClient.Client.LocalEndPoint).Port;
                    for (var j = 0; j < reservedPorts.Length; j++)
                    {
                        if (boundPort == reservedPorts[j])
                        {
                            isReservedPort = true;
                            break;
                        }
                    }
                }
                if (isReservedPort)
                {
                    _receivingUdpClient.Close();
                    _receivingUdpClient = null;
                    continue;
                }

                break;
            }
            catch (Exception)
            {
                // Failed in ten tries, throw the exception and give up
                if (i >= 9)
                {
                    throw;
                }

                Thread.Sleep(5);
            }
        }

        if (_receivingUdpClient == null)
        {
            throw new InvalidOperationException("Failed to bind a UDP port outside the reserved set after 10 attempts.");
        }

        // Receive from any IP on any port
        if (remoteIpEndPoint == null)
        {
            _remoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
        }
        else
        {
            _remoteIpEndPoint = remoteIpEndPoint;
        }

        _receivingUdpClient.Client.ReceiveTimeout = 10;
        _receivingUdpClient.Client.SendTimeout = 10;
        _maximumTransferUnit = 8192;
        _receivingUdpClient.Client.ReceiveBufferSize = 1024 * 1024;

        _receiveThread = Task.Run(ListenAsync, _cts.Token);
    }

    private async Task ListenAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var result = await _receivingUdpClient.ReceiveAsync(_cts.Token);

                if (result.Buffer != null && result.Buffer.Length > 0)
                {
                    try
                    {
                        OnBytesReceived(result.Buffer, result.RemoteEndPoint);
                    }
                    catch (Exception ex)
                    {
                        OnReceiveError(ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled, exit loop
                break;
            }
            catch (ObjectDisposedException)
            {
                // Ignore if disposed. This happens when closing the listener
            }
            catch (SocketException)
            {
                // This happens when a module terminates / crashes / is shut down
            }
            catch (Exception ex)
            {
                try
                {
                    OnReceiveError(ex);
                }
                catch
                {
                }
            }
        }
    }

    public void Close()
    {
        _cts.Cancel();
        lock (_callbackLock)
        {
            _receivingUdpClient?.Close();
        }

        try
        {
            _receiveThread?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
    }

    public void Dispose()
    {
        Close();
        _cts.Dispose();
        _closingEvent.Dispose();
    }

    private void SendData(in byte[] message, in IPEndPoint remoteEndpoint)
    {
        _receivingUdpClient.Send(message, message.Length, remoteEndpoint);
    }
    private void SendData(in byte[] message, in int remotePort)
    {
        _receivingUdpClient.Send(message, message.Length, new IPEndPoint(IPAddress.Loopback, remotePort));
    }

    public void SendData(in IpcPacket packet, in IPEndPoint remoteEndpoint)
    {
        if (_isConnected || packet.GetPacketType() == IpcPacket.PacketType.Handshake)
        {
            var packetData = packet.GetBytes();
            if (packetData.Length > _maximumTransferUnit)
            {
                // @TODO: Split packet into chunks
                var packetChunkBytes = PartialPacket.SplitPacketIntoChunks(packetData, _maximumTransferUnit);
                foreach (var packetChunk in packetChunkBytes)
                {
                    SendData(packetChunk, remoteEndpoint);
                }
            }
            else
            {
                SendData(packetData, remoteEndpoint);
            }
        }
        else
        {
            _eventBus.Push(packet);
        }
    }

    public void SendData(in IpcPacket packet, in int remotePort)
    {
        if (_isConnected || packet.GetPacketType() == IpcPacket.PacketType.Handshake)
        {
            var packetData = packet.GetBytes();
            if (packetData.Length > _maximumTransferUnit)
            {
                // @TODO: Split packet into chunks
                var packetChunkBytes = PartialPacket.SplitPacketIntoChunks(packetData, _maximumTransferUnit);
                foreach (var packetChunk in packetChunkBytes)
                {
                    SendData(packetChunk, new IPEndPoint(IPAddress.Loopback, remotePort));
                }
            }
            else
            {
                SendData(packetData, new IPEndPoint(IPAddress.Loopback, remotePort));
            }
        }
        else
        {
            _eventBus.Push(packet);
        }
    }

    public virtual void OnBytesReceived(in byte[] data, in IPEndPoint endpoint)
    {
        // @NOTE: Here for a class to extend and read data and handle it as needed
    }

    protected virtual void OnReceiveError(Exception ex)
    {
    }
}
