using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Osc;

namespace VRCFaceTracking.Core.Services;

public sealed partial class OscRecvService : BackgroundService
{
    private readonly ILogger<OscRecvService> _logger;
    private readonly IOscTarget _oscTarget;
    private readonly ILocalSettingsService _settingsService;

    private const int SioUdpConnReset = -1744830452;
    private const int ReceiveTimeoutMs = 250;
    private const int MaxDatagramSize = 65536;
    private const int MaxBundleDepth = 8;

    private Socket? _recvSocket;
    private readonly byte[] _recvBuffer = new byte[MaxDatagramSize];

    private CancellationToken _stoppingToken;

    public Action<OscMessage> OnMessageReceived { get; set; } = _ => { };
    public Action<int> OnMessagesReceived { get; set; } = _ => { };

    private readonly HashSet<string> _handledAddresses = new(StringComparer.Ordinal);
    private byte[][] _handledAddressBytes = [];

    public void HandleAddress(string address)
    {
        if (_handledAddresses.Add(address))
        {
            _handledAddressBytes = [.. _handledAddresses.Select(Encoding.UTF8.GetBytes)];
        }
    }

    private bool IsHandled(ReadOnlySpan<byte> datagram)
    {
        foreach (byte[] address in _handledAddressBytes)
        {
            if (OscCodec.AddressMatches(datagram, address))
            {
                return true;
            }
        }

        return false;
    }

    public OscRecvService(
        ILogger<OscRecvService> logger,
        IOscTarget oscTarget,
        ILocalSettingsService settingsService
    )
    {
        _logger = logger;
        _oscTarget = oscTarget;
        _settingsService = settingsService;

        _oscTarget.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(_oscTarget.IsConnected), StringComparison.Ordinal))
            {
                return;
            }

            var validationResults = new List<ValidationResult>();
            var context = new ValidationContext(_oscTarget);

            if (!Validator.TryValidateObject(_oscTarget, context, validationResults, validateAllProperties: true))
            {
                if (!string.Equals(_oscTarget.DestinationAddress, "127.0.0.1", StringComparison.Ordinal))
                {
                    _oscTarget.DestinationAddress = "127.0.0.1";
                }
                return;
            }

            if (IPAddress.TryParse(_oscTarget.DestinationAddress, out IPAddress? address))
            {
                UpdateTarget(new IPEndPoint(address, _oscTarget.InPort));
            }
        };
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await _settingsService.LoadAsync(_oscTarget, cancellationToken).ConfigureAwait(false);

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public IPEndPoint? UpdateTarget(IPEndPoint endpoint)
    {
        if (!IPAddress.IsLoopback(endpoint.Address))
        {
            IPAddress? local = LocalAddressFor(endpoint.Address);
            if (local == null)
            {
                LogNoLocalAddressCanReach(_logger, endpoint.Address);
                return null;
            }

            endpoint = new IPEndPoint(local, endpoint.Port);
        }

        LogUpdatingOscRecvTarget(_logger, endpoint);
        _recvSocket?.Close();
        _oscTarget.IsConnected = false;

        _recvSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ReceiveTimeout = ReceiveTimeoutMs,
        };
        if (OperatingSystem.IsWindows())
        {
            _recvSocket.IOControl((IOControlCode)SioUdpConnReset, [0, 0, 0, 0], optionOutValue: null);
        }

        try
        {
            _recvSocket.Bind(endpoint);
            _oscTarget.IsConnected = true;
            LogSuccessfullyConnectedRemoteEndpoint(_logger, _recvSocket.LocalEndPoint);
            return _recvSocket.LocalEndPoint as IPEndPoint;
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            LogCouldNotBindRecvEndpoint(_logger, endpoint, ex.Message);
        }

        return null;
    }

    public override void Dispose()
    {
        _recvSocket?.Dispose();
        base.Dispose();
    }

    private static IPAddress? LocalAddressFor(IPAddress destination)
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(destination, 9);
            return (probe.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        var completion = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                ReceiveLoop();
            }
            finally
            {
                completion.TrySetResult();
            }
        })
        {
            IsBackground = true,
            Name = "OSC Receive",
        };
        thread.Start();
        return completion.Task;
    }

    private void ReceiveLoop()
    {
        while (!_stoppingToken.IsCancellationRequested)
        {
            try
            {
                Socket? socket = _recvSocket;
                if (socket is not { IsBound: true })
                {
                    Thread.Sleep(50);
                    continue;
                }

                int bytesReceived;
                try
                {
                    bytesReceived = socket.Receive(_recvBuffer, SocketFlags.None);
                }
                catch (SocketException ex) when (ex.SocketErrorCode is SocketError.TimedOut or SocketError.ConnectionReset)
                {
                    continue;
                }

                OnMessagesReceived(1);
                Dispatch(0, bytesReceived, depth: 0);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException
                or SocketException { SocketErrorCode: SocketError.OperationAborted or SocketError.Interrupted })
            {
                Thread.Sleep(50);
            }
            catch (SocketException e)
            {
                LogErrorEncounteredOscReceiveThread(_logger, e);
                Thread.Sleep(500);
            }
        }
    }

    private void Dispatch(int start, int end, int depth)
    {
        ReadOnlySpan<byte> datagram = _recvBuffer.AsSpan(start, end - start);
        if (OscCodec.IsBundle(datagram))
        {
            if (depth >= MaxBundleDepth)
            {
                return;
            }

            int pos = start + OscCodec.BundleHeaderSize;
            while (OscCodec.TryReadBundleElement(_recvBuffer.AsSpan(0, end), ref pos, out int elementStart, out int elementEnd))
            {
                Dispatch(elementStart, elementEnd, depth + 1);
            }

            return;
        }

        int offset = start;
        if (IsHandled(datagram) && OscCodec.TryParse(_recvBuffer, end, ref offset, out OscMessage? message))
        {
            OnMessageReceived(message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No local address can reach {Address}; keeping the current recv target")]
    private static partial void LogNoLocalAddressCanReach(ILogger logger, IPAddress address);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updating osc recv target to {Endpoint}")]
    private static partial void LogUpdatingOscRecvTarget(ILogger logger, IPEndPoint endpoint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully connected to remote endpoint at {LocalEndPoint}")]
    private static partial void LogSuccessfullyConnectedRemoteEndpoint(ILogger logger, EndPoint? localEndPoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not bind to recv endpoint: {Endpoint}. {ErrorMessage}")]
    private static partial void LogCouldNotBindRecvEndpoint(ILogger logger, IPEndPoint endpoint, string errorMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error encountered in OSC Receive thread")]
    private static partial void LogErrorEncounteredOscReceiveThread(ILogger logger, Exception exception);
}
