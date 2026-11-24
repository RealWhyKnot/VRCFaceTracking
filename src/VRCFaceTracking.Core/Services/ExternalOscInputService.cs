using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Osc;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Services;

public sealed partial class ExternalOscInputService : IExternalTrackingSource, IDisposable
{
    private const int MaxBundleDepth = 8;
    private const int ReceiveTimeoutMs = 250;
    private const int MaxDatagramSize = 65535;
    private static readonly TimeSpan ReceivingWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StaleWindow = TimeSpan.FromSeconds(1);
    private static readonly int EyeChannelCount = Enum.GetValues<ExternalOscEyeChannel>().Length;

    private readonly ILogger<ExternalOscInputService> _logger;
    private readonly IOscTarget _target;
    private readonly ILocalSettingsService _settingsService;
    private readonly TimeProvider _time;
    private readonly Lock _stateLock = new();
    private readonly Lock _valuesLock = new();
    private readonly Lock _rateLock = new();
    private readonly Lock _publishLock = new();
    private readonly float[] _shapes = new float[(int)UnifiedExpressions.Max];
    private readonly bool[] _hasShape = new bool[(int)UnifiedExpressions.Max];
    private readonly long[] _shapeStamp = new long[(int)UnifiedExpressions.Max];
    private readonly float[] _eye = new float[EyeChannelCount];
    private readonly bool[] _hasEye = new bool[EyeChannelCount];
    private readonly long[] _eyeStamp = new long[EyeChannelCount];
    private readonly SignalCapabilityTracker _capabilities;
    private readonly AutoResetEvent _arrived = new(initialState: false);
    private readonly bool[] _driven = new bool[(int)UnifiedExpressions.Max];
    private readonly long _staleTicks;
    private bool _drivenEyeLeft;
    private bool _drivenEyeRight;
    private bool _anyDriven;
    private volatile bool _drivesEyes;
    private volatile bool _drivesFace;
    private bool _disposed;
    private int _releaseEpoch;

    private ReceiveSession? _session;
    private ExternalOscState _state;
    private string? _lastError;
    private long _lastPacket;
    private long _windowStart;
    private int _windowCount;
    private int _rate;
    private int _version;

    public ExternalOscInputService(ILogger<ExternalOscInputService> logger, ExternalOscInputSettings settings, IOscTarget target, ILocalSettingsService settingsService, TimeProvider? time = null, SignalCapabilityTracker? capabilities = null)
    {
        _logger = logger;
        Settings = settings;
        _target = target;
        _settingsService = settingsService;
        _time = time ?? TimeProvider.System;
        _capabilities = capabilities ?? SignalCapabilityTracker.Shared;
        _staleTicks = (long)(StaleWindow.TotalSeconds * _time.TimestampFrequency);
        _target.PropertyChanged += OnTargetChanged;
    }

    public ExternalOscInputSettings Settings { get; }

    public int Version => Volatile.Read(ref _version);

    public WaitHandle DataArrived => _arrived;

    public bool DrivesEyes => _drivesEyes;

    public bool DrivesFace => _drivesFace;

    public TimeSpan? SinceLastData
    {
        get
        {
            if (Volatile.Read(ref _session) == null || !Settings.Enabled)
            {
                return null;
            }

            lock (_rateLock)
            {
                return _lastPacket == 0 ? null : _time.GetElapsedTime(_lastPacket);
            }
        }
    }

    public ExternalOscStatus Status
    {
        get
        {
            lock (_stateLock)
            {
                lock (_rateLock)
                {
                    RollWindow(_time.GetTimestamp());
                    ExternalOscState state = _state;
                    if (state == ExternalOscState.Listening && _lastPacket != 0 && _time.GetElapsedTime(_lastPacket) < ReceivingWindow)
                    {
                        state = ExternalOscState.Receiving;
                    }

                    string? fault = _session?.Fault;
                    return fault != null
                        ? new ExternalOscStatus(ExternalOscState.Error, 0, fault)
                        : new ExternalOscStatus(state, state == ExternalOscState.Receiving ? _rate : 0, _lastError);
                }
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _settingsService.LoadAsync(Settings, cancellationToken).ConfigureAwait(false);
        Apply();
    }

    public async Task SaveAsync(CancellationToken cancellationToken) =>
        await _settingsService.SaveAsync(Settings, cancellationToken).ConfigureAwait(false);

    public bool Apply()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return false;
            }

            StopLocked();
            if (!Settings.Enabled)
            {
                _state = ExternalOscState.Disabled;
                _lastError = null;
                return true;
            }

            ExternalOscPortCheck check = ExternalOscInputSettings.Check(Settings.Port, _target);
            if (check != ExternalOscPortCheck.Valid)
            {
                _state = ExternalOscState.Error;
                _lastError = check.ToString();
                return false;
            }

            return TryBind(Settings.Port);
        }
    }

    public bool TryGetShape(UnifiedExpressions expression, out float value)
    {
        int index = (int)expression;
        lock (_valuesLock)
        {
            bool has = index >= 0 && index < _shapes.Length && _hasShape[index];
            value = has ? _shapes[index] : 0f;
            return has;
        }
    }

    public IReadOnlyDictionary<UnifiedExpressions, float> ShapeSnapshot()
    {
        Dictionary<UnifiedExpressions, float> snapshot = [];
        lock (_valuesLock)
        {
            for (int i = 0; i < _shapes.Length; i++)
            {
                if (_hasShape[i])
                {
                    snapshot[(UnifiedExpressions)i] = _shapes[i];
                }
            }
        }

        return snapshot;
    }

    public IReadOnlyDictionary<ExternalOscEyeChannel, float> EyeSnapshot()
    {
        Dictionary<ExternalOscEyeChannel, float> snapshot = [];
        lock (_valuesLock)
        {
            for (int i = 0; i < _eye.Length; i++)
            {
                if (_hasEye[i])
                {
                    snapshot[(ExternalOscEyeChannel)i] = _eye[i];
                }
            }
        }

        return snapshot;
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            StopLocked();
            _state = ExternalOscState.Disabled;
            if (!_disposed)
            {
                _disposed = true;
                _target.PropertyChanged -= OnTargetChanged;
                _arrived.Dispose();
            }
        }
    }

    internal bool Ingest(byte[] buffer, int length)
    {
        bool any = false;
        Dispatch(buffer, 0, length, depth: 0, ref any);
        if (any)
        {
            Interlocked.Increment(ref _version);
            Count();
            _arrived.Set();
        }

        return any;
    }

    private void OnTargetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IOscTarget.InPort) or nameof(IOscTarget.OutPort) or null or "")
        {
            Revalidate();
        }
    }

    private void Revalidate()
    {
        lock (_stateLock)
        {
            if (_disposed || !Settings.Enabled)
            {
                return;
            }

            bool valid = ExternalOscInputSettings.Check(Settings.Port, _target) == ExternalOscPortCheck.Valid;
            bool running = _session != null;
            bool blockedByConflict = _state == ExternalOscState.Error && string.Equals(_lastError, nameof(ExternalOscPortCheck.Conflicting), StringComparison.Ordinal);
            if ((running && !valid) || (!running && valid && blockedByConflict))
            {
                Apply();
            }
        }
    }

    private bool TryBind(int port)
    {
        Socket? socket = null;
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                ReceiveTimeout = ReceiveTimeoutMs,
            };
            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            var session = new ReceiveSession(socket);
            socket = null;
            _state = ExternalOscState.Listening;
            _lastError = null;
            ResetRate();
            ClearValues();
            session.Thread = new Thread(() => ReceiveLoop(session))
            {
                IsBackground = true,
                Name = "External OSC input",
            };
            _session = session;
            session.Thread.Start();
            LogListening(_logger, port);
            return true;
        }
        catch (SocketException ex)
        {
            socket?.Dispose();
            _state = ExternalOscState.Error;
            _lastError = ex.SocketErrorCode.ToString();
            LogBindFailed(_logger, port, ex.SocketErrorCode);
            return false;
        }
    }

    private void StopLocked()
    {
        ReceiveSession? session = _session;
        _session = null;
        if (session != null)
        {
            session.Stop();
            session.Thread?.Join();
            ClearValues();
        }

        ReleaseDriven();
    }

    private void ClearValues()
    {
        lock (_valuesLock)
        {
            Array.Clear(_hasShape);
            Array.Clear(_shapes);
            Array.Clear(_shapeStamp);
            Array.Clear(_eye);
            Array.Clear(_hasEye);
            Array.Clear(_eyeStamp);
        }
    }

    private void ReceiveLoop(ReceiveSession session)
    {
        Socket socket = session.Socket;
        byte[] buffer = new byte[MaxDatagramSize];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (!session.IsStopping)
        {
            int length;
            try
            {
                length = socket.ReceiveFrom(buffer, ref remote);
            }
            catch (SocketException ex) when (!session.IsStopping && ex.SocketErrorCode is SocketError.TimedOut or SocketError.ConnectionReset)
            {
                continue;
            }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException)
            {
                if (!session.IsStopping)
                {
                    session.Fault = ex is SocketException socketEx ? socketEx.SocketErrorCode.ToString() : nameof(ObjectDisposedException);
                    LogReceiveFailed(_logger, session.Fault);
                }

                return;
            }

            if (session.IsStopping)
            {
                return;
            }

            Ingest(buffer, length);
        }
    }

    private sealed class ReceiveSession
    {
        private volatile bool _isStopping;

        public ReceiveSession(Socket socket)
        {
            Socket = socket;
        }

        public Socket Socket { get; }

        public Thread? Thread { get; set; }

        public bool IsStopping => _isStopping;

        public volatile string? Fault;

        public void Stop()
        {
            _isStopping = true;
            Socket.Dispose();
        }
    }

    private void ResetRate()
    {
        lock (_rateLock)
        {
            _lastPacket = 0;
            _windowStart = 0;
            _windowCount = 0;
            _rate = 0;
        }
    }

    private void Count()
    {
        lock (_rateLock)
        {
            long now = _time.GetTimestamp();
            _lastPacket = now;
            RollWindow(now);
            _windowCount++;
        }
    }

    private void RollWindow(long now)
    {
        if (_windowStart == 0)
        {
            _windowStart = now;
            return;
        }

        TimeSpan elapsed = _time.GetElapsedTime(_windowStart, now);
        if (elapsed >= TimeSpan.FromSeconds(1))
        {
            _rate = (int)Math.Round(_windowCount / elapsed.TotalSeconds);
            _windowCount = 0;
            _windowStart = now;
        }
    }

    private void Dispatch(byte[] buffer, int start, int end, int depth, ref bool any)
    {
        ReadOnlySpan<byte> datagram = buffer.AsSpan(start, end - start);
        if (OscCodec.IsBundle(datagram))
        {
            if (depth >= MaxBundleDepth)
            {
                return;
            }

            int pos = start + OscCodec.BundleHeaderSize;
            while (OscCodec.TryReadBundleElement(buffer.AsSpan(0, end), ref pos, out int elementStart, out int elementEnd))
            {
                Dispatch(buffer, elementStart, elementEnd, depth + 1, ref any);
            }

            return;
        }

        int offset = start;
        if (OscCodec.TryParse(buffer, end, ref offset, out OscMessage? message) && message.Value is float value && float.IsFinite(value))
        {
            any |= Store(message.Address, value);
        }
    }

    private bool Store(string address, float value)
    {
        ExternalOscTarget? target = ExternalOscParameterMap.Resolve(address);
        if (target == null)
        {
            return false;
        }

        long now = _time.GetTimestamp();
        lock (_valuesLock)
        {
            if (target.Lid is { } lid)
            {
                StoreLidLocked(lid, lid.Decode(value), now);
            }

            foreach (UnifiedExpressions shape in target.Shapes)
            {
                _shapes[(int)shape] = Math.Clamp(value, 0f, 1f);
                _shapeStamp[(int)shape] = now;
                _hasShape[(int)shape] = true;
            }

            if (target.EyeChannel is { } channel)
            {
                PutEyeLocked(channel, Math.Clamp(value, -1f, 1f), now);
            }
        }

        return true;
    }

    private void StoreLidLocked(ExternalOscLid lid, ExternalOscLidValue decoded, long now)
    {
        (ExternalOscEyeChannel openness, ExternalOscEyeChannel widen, ExternalOscEyeChannel squint) = lid.Side switch
        {
            ExternalOscLidSide.Left => (lid.Format == ExternalOscLidFormat.Closed ? ExternalOscEyeChannel.LeftOpenness : ExternalOscEyeChannel.LeftOpennessDerived, ExternalOscEyeChannel.LeftWiden, ExternalOscEyeChannel.LeftSquint),
            ExternalOscLidSide.Right => (lid.Format == ExternalOscLidFormat.Closed ? ExternalOscEyeChannel.RightOpenness : ExternalOscEyeChannel.RightOpennessDerived, ExternalOscEyeChannel.RightWiden, ExternalOscEyeChannel.RightSquint),
            ExternalOscLidSide.Both => (lid.Format == ExternalOscLidFormat.Closed ? ExternalOscEyeChannel.CombinedOpenness : ExternalOscEyeChannel.CombinedOpennessDerived, ExternalOscEyeChannel.CombinedWiden, ExternalOscEyeChannel.CombinedSquint),
            _ => (lid.Format == ExternalOscLidFormat.Closed ? ExternalOscEyeChannel.CombinedOpenness : ExternalOscEyeChannel.CombinedOpennessDerived, ExternalOscEyeChannel.CombinedWiden, ExternalOscEyeChannel.CombinedSquint),
        };

        PutEyeLocked(openness, decoded.Openness, now);
        if (decoded.Widen is { } widenValue)
        {
            PutEyeLocked(widen, widenValue, now);
        }

        if (decoded.Squint is { } squintValue)
        {
            PutEyeLocked(squint, squintValue, now);
        }
    }

    private void PutEyeLocked(ExternalOscEyeChannel channel, float value, long now)
    {
        _eye[(int)channel] = value;
        _hasEye[(int)channel] = true;
        _eyeStamp[(int)channel] = now;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "External OSC input listening on port {Port}")]
    private static partial void LogListening(ILogger logger, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "External OSC input stopped receiving ({Error})")]
    private static partial void LogReceiveFailed(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "External OSC input could not use port {Port} ({Error})")]
    private static partial void LogBindFailed(ILogger logger, int port, SocketError error);
}
