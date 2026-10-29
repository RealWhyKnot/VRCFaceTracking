using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Sandboxing.Ipc;

namespace VRCFaceTracking.Core.Sandboxing;

public sealed partial class VrcftSandboxServer : UdpFullDuplex
{
    private readonly ILogger<VrcftSandboxServer> _logger;
    public OnPacketReceived? OnPacketReceived { get; set; }
    public VrcftSandboxServer(ILoggerFactory factory, int[] reservedPorts) : base(0, reservedPorts)
    {
        _logger = factory.CreateLogger<VrcftSandboxServer>();

        LogStartingSandboxHostPort(_logger, Port);
        IsConnected = true;
    }

    protected override void OnReceiveError(SocketException ex) => LogSandboxReceiveFailed(_logger, ex.SocketErrorCode);

    public override void OnBytesReceived(in byte[] data, in IPEndPoint endpoint)
    {
        if (VrcftPacketDecoder.TryDecodePacket(data, out IpcPacket? packet))
        {
            if (packet.GetPacketType() == IpcPacket.PacketType.Handshake)
            {
                var handshakePacket = (HandshakePacket)packet;
                if (handshakePacket.IsValid)
                {
                    LogReceivedHandshakePortSendingAck(_logger, endpoint.Port);
                    SendData(in packet, in endpoint);
                }
            }
            if (OnPacketReceived != null)
            {
                if (packet.GetPacketType() == IpcPacket.PacketType.SplitPacketChunk)
                {
                    PartialPacket.DecodePacket(data, out byte[]? combinedData, endpoint.Port);
                    if (combinedData.Length > 0)
                    {
                        OnBytesReceived(combinedData, endpoint);
                    }
                }
                else
                {
                    OnPacketReceived(packet, endpoint.Port);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting sandbox host on port {Port}...")]
    private static partial void LogStartingSandboxHostPort(ILogger logger, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sandbox socket receive failed ({Error})")]
    private static partial void LogSandboxReceiveFailed(ILogger logger, SocketError error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Received handshake from port {EndpointPort}. Sending ACK...")]
    private static partial void LogReceivedHandshakePortSendingAck(ILogger logger, int endpointPort);
}
