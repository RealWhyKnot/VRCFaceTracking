using System.Net;
using System.Net.Sockets;
using VRCFaceTracking.Core.Sandboxing.Ipc;

namespace VRCFaceTracking.Core.Sandboxing;


public class UdpFullDuplex : IDisposable
{
    private const int SioUdpConnReset = -1744830452;
    private const int EthernetFrameSize = 1500;
    internal const int ReceiveTimeoutMs = 250;

    public int Port
    {
        get;
    }

    private readonly Lock _callbackLock;

    protected UdpClient ReceivingUdpClient { get; }
    private readonly ManualResetEvent _closingEvent;
    protected bool IsConnected { get; set; }
    protected SimpleEventBus EventBus { get; } = new();
    private readonly Thread _receiveThread;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;
    public int Mtu { get; } = EthernetFrameSize;

    public UdpFullDuplex(int port, int[]? reservedPorts = null)
    {
        _closingEvent = new ManualResetEvent(initialState: false);
        _callbackLock = new Lock();

        UdpClient? client = null;
        for (int i = 0; i < 10; i++)
        {
            try
            {
                client = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                if (OperatingSystem.IsWindows())
                {
                    client.Client.IOControl((IOControlCode)SioUdpConnReset, [0, 0, 0, 0], optionOutValue: null);
                }
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, optionValue: true);

                bool isReservedPort = false;
                if (reservedPorts != null)
                {
                    int boundPort = LocalPortOf(client);
                    for (int j = 0; j < reservedPorts.Length; j++)
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
                    client.Close();
                    client = null;
                    continue;
                }

                break;
            }
            catch (SocketException) when (i < 9)
            {
                Thread.Sleep(5);
            }
        }

        ReceivingUdpClient = client ?? throw new InvalidOperationException("Failed to bind a UDP port outside the reserved set after 10 attempts.");
        Port = LocalPortOf(client);

        ReceivingUdpClient.Client.ReceiveTimeout = ReceiveTimeoutMs;
        ReceivingUdpClient.Client.SendTimeout = 10;
        Mtu = 8192;
        ReceivingUdpClient.Client.ReceiveBufferSize = 1024 * 1024;

        _receiveThread = new Thread(Listen)
        {
            IsBackground = true,
            Name = "IPC Receive",
        };
        _receiveThread.Start();
    }

    private static int LocalPortOf(UdpClient client) => client.Client.LocalEndPoint is IPEndPoint local ? local.Port : 0;

    private void Listen()
    {
        var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        while (!_cts.IsCancellationRequested)
        {
            byte[] buffer;
            try
            {
                buffer = ReceivingUdpClient.Receive(ref remoteEndPoint);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut)
            {
                continue;
            }
            catch (SocketException e)
            {
                OnReceiveError(e);
                continue;
            }

            if (buffer.Length > 0)
            {
                OnBytesReceived(buffer, remoteEndPoint);
            }
        }
    }

    public void Close()
    {
        _cts.Cancel();
        lock (_callbackLock)
        {
            ReceivingUdpClient?.Close();
        }

        _receiveThread.Join(TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (disposing)
        {
            Close();
            _cts.Dispose();
            _closingEvent.Dispose();
        }
    }

    private void SendData(in byte[] message, in IPEndPoint remoteEndpoint) => ReceivingUdpClient.Send(message, message.Length, remoteEndpoint);

    public void SendData(in IpcPacket packet, in IPEndPoint remoteEndpoint)
    {
        if (IsConnected || packet.GetPacketType() == IpcPacket.PacketType.Handshake)
        {
            byte[] packetData = packet.GetBytes();
            if (packetData.Length > Mtu)
            {
                byte[][] packetChunkBytes = PartialPacket.SplitPacketIntoChunks(packetData, Mtu);
                foreach (byte[] packetChunk in packetChunkBytes)
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
            EventBus.Push(packet);
        }
    }

    public void SendData(in IpcPacket packet, in int remotePort)
    {
        if (IsConnected || packet.GetPacketType() == IpcPacket.PacketType.Handshake)
        {
            byte[] packetData = packet.GetBytes();
            if (packetData.Length > Mtu)
            {
                byte[][] packetChunkBytes = PartialPacket.SplitPacketIntoChunks(packetData, Mtu);
                foreach (byte[] packetChunk in packetChunkBytes)
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
            EventBus.Push(packet);
        }
    }

    public virtual void OnBytesReceived(in byte[] data, in IPEndPoint endpoint)
    {
    }

    protected virtual void OnReceiveError(SocketException ex)
    {
    }
}
