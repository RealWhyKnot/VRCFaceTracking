using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.OSC;

namespace VRCFaceTracking.Core.Services;

/**
 * OscSendService is responsible for encoding osc messages and sending them over OSC
 */
public class OscSendService
{
    private readonly ILogger<OscSendService> _logger;
    private readonly IOscTarget _oscTarget;

    private Socket _sendSocket;
    private readonly byte[] _sendBuffer = new byte[4096];

    public Action<int> OnMessagesDispatched = _ => { };

    public OscSendService(
        ILogger<OscSendService> logger,
        IOscTarget oscTarget
    )
    {
        _logger = logger;
        _oscTarget = oscTarget;

        _oscTarget.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not (nameof(IOscTarget.OutPort) or nameof(IOscTarget.DestinationAddress)))
            {
                return;
            }

            if (_oscTarget.OutPort == default
                || !IPAddress.TryParse(_oscTarget.DestinationAddress, out var address)
                || address.AddressFamily != AddressFamily.InterNetwork)
            {
                return;
            }

            UpdateTarget(new IPEndPoint(address, _oscTarget.OutPort));
        };
    }

    private void UpdateTarget(IPEndPoint endpoint)
    {
        _oscTarget.IsConnected = false;

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.Connect(endpoint);
            _oscTarget.IsConnected = true;
        }
        catch (SocketException ex)
        {
            _logger.LogWarning($"Failed to bind to sender endpoint: {endpoint}. {ex.Message}");
        }

        Interlocked.Exchange(ref _sendSocket, socket)?.Close();
    }

    public async Task Send(OscMessage message, CancellationToken ct)
    {
        var nextByteIndex = message.Encode(_sendBuffer);
        if (nextByteIndex < 0)
        {
            _logger.LogError("OSC message too large to send! Skipping this batch of messages.");
            return;
        }

        if (_sendSocket == null)
        {
            return;
        }

        await _sendSocket.SendAsync(_sendBuffer.AsMemory(0, nextByteIndex), SocketFlags.None, ct);
        OnMessagesDispatched(1);
    }

    public void Send(List<OscMessage> messages)
    {
        var socket = _sendSocket;
        if (socket == null || messages.Count == 0)
        {
            return;
        }

        var index = 0;
        while (index < messages.Count)
        {
            var lastIndex = index;
            var length = OscCodec.EncodeBundle(_sendBuffer, messages, ref index);
            if (index <= lastIndex)
            {
                _logger.LogError("OSC message {Index} of {Count} is too large to encode; skipping it", lastIndex, messages.Count);
                index = lastIndex + 1;
                continue;
            }

            if (length <= 0)
            {
                _logger.LogError("OSC bundle encoding failed at message {Index} of {Count}", index, messages.Count);
                break;
            }

            socket.Send(_sendBuffer.AsSpan(0, length), SocketFlags.None);
        }

        OnMessagesDispatched(index);
    }
}
