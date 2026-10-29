using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.OSC;

namespace VRCFaceTracking.Core.Services;

public class OscRecvService : BackgroundService
{
    private readonly ILogger<OscRecvService> _logger;
    private readonly IOscTarget _oscTarget;
    private readonly ILocalSettingsService _settingsService;

    private const int SIO_UDP_CONNRESET = -1744830452;
    private const int ReceiveTimeoutMs = 250;

    private Socket _recvSocket;
    private readonly byte[] _recvBuffer = new byte[4096];

    private CancellationTokenSource _cts, _linkedToken;
    private CancellationToken _stoppingToken;

    public Action<OscMessage> OnMessageReceived = _ => { };
    public Action<int> OnMessagesReceived = _ => { };

    private readonly HashSet<string> _handledAddresses = new(StringComparer.Ordinal);
    private byte[][] _handledAddressBytes = Array.Empty<byte[]>();

    public void HandleAddress(string address)
    {
        if (_handledAddresses.Add(address))
        {
            _handledAddressBytes = _handledAddresses.Select(Encoding.UTF8.GetBytes).ToArray();
        }
    }

    private bool IsHandled(byte[] buffer, int length)
    {
        var span = buffer.AsSpan(0, Math.Min(length, buffer.Length));
        foreach (var address in _handledAddressBytes)
        {
            if (OscCodec.AddressMatches(span, address))
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
        _cts = new CancellationTokenSource();

        _oscTarget = oscTarget;
        _settingsService = settingsService;

        _oscTarget.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(_oscTarget.IsConnected))
                return;

            var validationResults = new List<ValidationResult>();
            var context = new ValidationContext(_oscTarget);

            if (!Validator.TryValidateObject(_oscTarget, context, validationResults, validateAllProperties: true))
            {
                var errorMessages = string.Join(Environment.NewLine, validationResults.Select(v => v.ErrorMessage));
                //_logger.LogWarning($"{errorMessages} Reverting to default.");
                if (_oscTarget.DestinationAddress != "127.0.0.1")
                {
                    _oscTarget.DestinationAddress = "127.0.0.1";
                }
                return;
            }

            UpdateTarget(new IPEndPoint(IPAddress.Parse(_oscTarget.DestinationAddress), _oscTarget.InPort));
        };
    }

    public async override Task StartAsync(CancellationToken cancellationToken)
    {
        await _settingsService.Load(_oscTarget);

        await base.StartAsync(cancellationToken);
    }

    public IPEndPoint UpdateTarget(IPEndPoint endpoint)
    {
        if (!IPAddress.IsLoopback(endpoint.Address))
        {
            var local = LocalAddressFor(endpoint.Address);
            if (local == null)
            {
                _logger.LogWarning("No local address can reach {Address}; keeping the current recv target", endpoint.Address);
                return null;
            }

            endpoint = new IPEndPoint(local, endpoint.Port);
        }

        _logger.LogInformation($"Updating osc recv target to {endpoint}");
        _cts.Cancel();
        _recvSocket?.Close();
        _oscTarget.IsConnected = false;

        _recvSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ReceiveTimeout = ReceiveTimeoutMs,
        };
        if (OperatingSystem.IsWindows())
        {
            _recvSocket.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
        }

        try
        {
            _recvSocket.Bind(endpoint);
            _oscTarget.IsConnected = true;
            _logger.LogInformation($"Successfully connected to remote endpoint at {_recvSocket.LocalEndPoint}");
            return (IPEndPoint)_recvSocket.LocalEndPoint;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not bind to recv endpoint: {endpoint}. {ex.Message}");
        }
        finally
        {
            var oldCts = _cts;
            var oldLinked = _linkedToken;
            _cts = new CancellationTokenSource();
            _linkedToken = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken, _cts.Token);
            oldCts.Dispose();
            oldLinked?.Dispose();
        }

        return null;
    }

    private static IPAddress LocalAddressFor(IPAddress destination)
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(destination, 9);
            return ((IPEndPoint)probe.LocalEndPoint).Address;
        }
        catch (Exception)
        {
            return null;
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        _linkedToken = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken, _cts.Token);

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
                var socket = _recvSocket;
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

                if (!IsHandled(_recvBuffer, bytesReceived))
                {
                    continue;
                }

                var offset = 0;
                var newMsg = OscMessage.TryParseOsc(_recvBuffer, bytesReceived, ref offset);
                if (newMsg == null)
                {
                    continue;
                }

                OnMessageReceived(newMsg);
            }
            catch (Exception e)
            {
                if (e is OperationCanceledException or ObjectDisposedException
                    or SocketException { SocketErrorCode: SocketError.OperationAborted or SocketError.Interrupted })
                {
                    Thread.Sleep(50);
                    continue;
                }

                _logger.LogError(e, "Error encountered in OSC Receive thread");
                Thread.Sleep(500);
            }
        }
    }
}
