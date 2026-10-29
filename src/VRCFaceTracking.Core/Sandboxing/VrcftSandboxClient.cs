using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Sandboxing.Ipc;

namespace VRCFaceTracking.Core.Sandboxing;

public sealed partial class VrcftSandboxClient : UdpFullDuplex
{
    private readonly IPEndPoint _serverEndpoint;
    private readonly ILogger<VrcftSandboxClient> _logger;

    public OnPacketReceivedCallback? OnPacketReceivedCallback { get; set; }
    public VrcftSandboxClient(int portNumber,
        ILoggerFactory factory
        ) : base(0)
    {
        _logger = factory.CreateLogger<VrcftSandboxClient>();

        _serverEndpoint = new IPEndPoint(IPAddress.Loopback, portNumber);

        LogStartingSandboxProcessPort(_logger, Port);
    }

    public void Connect(in string modulePath)
    {
        var handshakePkt = new HandshakePacket
        {
            ModulePath = modulePath,
        };
        LogAttemptingConnectServer(_logger);
        SendData(handshakePkt, _serverEndpoint);
    }

    protected override void OnReceiveError(SocketException ex) => LogSandboxReceiveFailed(_logger, ex.SocketErrorCode);

    public override void OnBytesReceived(in byte[] data, in IPEndPoint endpoint)
    {
        if (VrcftPacketDecoder.TryDecodePacket(data, out IpcPacket? packet))
        {
            if (OnPacketReceivedCallback != null && packet.GetPacketType() != IpcPacket.PacketType.Unknown)
            {
                if (packet.GetPacketType() == IpcPacket.PacketType.SplitPacketChunk)
                {
                    PartialPacket.DecodePacket(data, out byte[]? combinedData);
                    if (combinedData.Length > 0)
                    {
                        OnBytesReceived(combinedData, endpoint);
                    }
                }
                else
                {
                    OnPacketReceivedCallback(packet);
                }
            }

            if (packet.GetPacketType() == IpcPacket.PacketType.Handshake)
            {
                var handshakePacket = (HandshakePacket)packet;
                if (handshakePacket.IsValid)
                {
                    LogReceivedAckHostPortHandshake(_logger, endpoint.Port);
                    IsConnected = true;
                    SendAllPendingPackets();
                }
            }
        }
    }

    private const int ChunkSpacingMicroseconds = 50;

    private static void SpaceChunk()
    {
        long target = System.Diagnostics.Stopwatch.GetTimestamp()
            + (System.Diagnostics.Stopwatch.Frequency * ChunkSpacingMicroseconds / 1_000_000L);
        while (System.Diagnostics.Stopwatch.GetTimestamp() < target)
        {
            Thread.SpinWait(50);
        }
    }

    private void SendData(in byte[] message) => ReceivingUdpClient.Send(message, message.Length, _serverEndpoint);
    public void SendData(in IpcPacket packet)
    {
        if (IsConnected || packet.GetPacketType() == IpcPacket.PacketType.Handshake)
        {
            byte[] packetData = packet.GetBytes();
            if (packetData.Length > Mtu)
            {
                byte[][] packetChunkBytes = PartialPacket.SplitPacketIntoChunks(packetData, Mtu);
                foreach (byte[] packetChunk in packetChunkBytes)
                {
                    SendData(packetChunk);
                    SpaceChunk();
                }
            }
            else
            {
                SendData(packetData);
            }
        }
        else
        {
            EventBus.Push(packet);
        }
    }

    public void SendAllPendingPackets()
    {
        if (IsConnected)
        {
            while (EventBus.TryPop(out IpcPacket? pkt))
            {
                SendData(pkt);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting sandbox process on port {Port}...")]
    private static partial void LogStartingSandboxProcessPort(ILogger logger, int port);

    [LoggerMessage(Level = LogLevel.Information, Message = "Attempting to connect to server...")]
    private static partial void LogAttemptingConnectServer(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sandbox socket receive failed ({Error})")]
    private static partial void LogSandboxReceiveFailed(ILogger logger, SocketError error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Received ACK from host on port {EndpointPort}. Handshake done.")]
    private static partial void LogReceivedAckHostPortHandshake(ILogger logger, int endpointPort);
}
