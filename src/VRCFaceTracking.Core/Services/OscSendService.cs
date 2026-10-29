using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Osc;

namespace VRCFaceTracking.Core.Services;

public sealed partial class OscSendService
{
    private readonly ILogger<OscSendService> _logger;
    private readonly IOscTarget _oscTarget;

    private Socket? _sendSocket;
    private readonly byte[] _sendBuffer = new byte[4096];

    public Action<int> OnMessagesDispatched { get; set; } = _ => { };

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
                || !IPAddress.TryParse(_oscTarget.DestinationAddress, out IPAddress? address)
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
            LogFailedBindSenderEndpoint(_logger, endpoint, ex.Message);
        }

        Interlocked.Exchange(ref _sendSocket, socket)?.Close();
    }

    public void Send(IReadOnlyList<OscMessage> messages)
    {
        Socket? socket = _sendSocket;
        if (socket == null || messages.Count == 0)
        {
            return;
        }

        int index = 0;
        while (index < messages.Count)
        {
            int lastIndex = index;
            int length = OscCodec.EncodeBundle(_sendBuffer, messages, ref index);
            if (index <= lastIndex)
            {
                LogOscMessageTooLargeEncode(_logger, lastIndex, messages.Count);
                index = lastIndex + 1;
                continue;
            }

            if (length <= 0)
            {
                LogOscBundleEncodingFailedMessage(_logger, index, messages.Count);
                break;
            }

            socket.Send(_sendBuffer.AsSpan(0, length), SocketFlags.None);
        }

        OnMessagesDispatched(index);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to bind to sender endpoint: {Endpoint}. {ErrorMessage}")]
    private static partial void LogFailedBindSenderEndpoint(ILogger logger, IPEndPoint endpoint, string errorMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "OSC message {Index} of {Count} is too large to encode; skipping it")]
    private static partial void LogOscMessageTooLargeEncode(ILogger logger, int index, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "OSC bundle encoding failed at message {Index} of {Count}")]
    private static partial void LogOscBundleEncodingFailedMessage(ILogger logger, int index, int count);
}
