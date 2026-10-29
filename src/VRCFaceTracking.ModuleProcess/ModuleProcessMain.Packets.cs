using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;

namespace VRCFaceTracking.ModuleProcess;

internal sealed partial class ModuleProcessMain
{
    private static void OnHostPacket(IpcPacket packet, ExtTrackingModule trackingModule, ModuleAssembly moduleAssembly, VrcftSandboxClient client, ILogger fileLogger)
    {
        Connected = true;
        ConnectionTimer?.Change(TimeSpan.FromSeconds(ConnectedTimeoutSeconds), Timeout.InfiniteTimeSpan);

        switch (packet.GetPacketType())
        {
            case IpcPacket.PacketType.EventGetSupported:
                ReplySupported(trackingModule);
                break;
            case IpcPacket.PacketType.EventInit:
                InitializeModule((EventInitPacket)packet, trackingModule, moduleAssembly, client, fileLogger);
                break;
            case IpcPacket.PacketType.EventTeardown:
                TeardownModule(trackingModule, moduleAssembly, client);
                break;
            case IpcPacket.PacketType.EventUpdate:
                QueueLatestUpdate();
                break;
            case IpcPacket.PacketType.EventSetImageStream:
                ImageStreamEnabled = ((EventSetImageStreamPacket)packet).Enabled;
                break;
            case IpcPacket.PacketType.EventUpdateStatus:
                ApplyStatus((EventStatusUpdatePacket)packet, trackingModule);
                break;
            case IpcPacket.PacketType.EventSetVerbose:
                SetVerbose(((EventSetVerbosePacket)packet).Verbose);
                break;
            case IpcPacket.PacketType.Unknown:
            case IpcPacket.PacketType.Handshake:
            case IpcPacket.PacketType.SplitPacketChunk:
            case IpcPacket.PacketType.EventLog:
            case IpcPacket.PacketType.ReplyGetSupported:
            case IpcPacket.PacketType.ReplyInit:
            case IpcPacket.PacketType.ReplyUpdate:
            case IpcPacket.PacketType.ReplyTeardown:
            case IpcPacket.PacketType.DebugStreamFrame:
            default:
                break;
        }
    }

    private static void ReplySupported(ExtTrackingModule trackingModule)
    {
        (bool supportsEye, bool supportsExpression) = trackingModule.Supported;
        PacketsToSend.Enqueue(new ReplySupportedPacket()
        {
            EyeAvailable = supportsEye,
            ExpressionAvailable = supportsExpression,
        });
        Wakeup.Set();
    }

    private static void InitializeModule(EventInitPacket init, ExtTrackingModule trackingModule, ModuleAssembly moduleAssembly, VrcftSandboxClient client, ILogger fileLogger)
    {
        bool eyeSuccess, expressionSuccess;
        try
        {
            (eyeSuccess, expressionSuccess) = trackingModule.Initialize(init.EyeAvailable, init.ExpressionAvailable);
        }
        catch (MissingMethodException)
        {
            LogDoesNotProperlyImplementExtTrackingModule(Logger, trackingModule.GetType().Name);
            return;
        }

        var updateCts = new CancellationTokenSource();
        moduleAssembly.UpdateCts = updateCts;
        CancellationToken updateToken = updateCts.Token;
        UpdateThread = new Thread(() => RunUpdateLoop(trackingModule, client, fileLogger, updateToken))
        {
            IsBackground = true,
        };
        UpdateThread.Start();

        PacketsToSend.Enqueue(new ReplyInitPacket()
        {
            EyeSuccess = eyeSuccess,
            ExpressionSuccess = expressionSuccess,
            ModuleInformationName = trackingModule.ModuleInformation.Name,
            IconDataStreams = trackingModule.ModuleInformation.StaticImages ?? [],
        });
        Wakeup.Set();
    }

    private static void RunUpdateLoop(ExtTrackingModule trackingModule, VrcftSandboxClient client, ILogger fileLogger, CancellationToken cancellationToken)
    {
        var snapshot = new UnifiedTrackingData();
        byte[] scratch = new byte[ReplyUpdatePacket.PayloadSize];
        var timerResolution = new TimerResolutionHold(Logger);
        UpdateLoopDiagnostics? diagnostics = Logger.IsEnabled(LogLevel.Debug) ? new UpdateLoopDiagnostics(Logger) : null;
        bool sendFailing = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            long t0 = Stopwatch.GetTimestamp();
            trackingModule.Update();
            long t1 = Stopwatch.GetTimestamp();

            using (UnifiedTracking.LockData())
            {
                snapshot.CopyPropertiesOf(UnifiedTracking.Data);
            }

            var changed = ReplyUpdatePacket.CaptureIfChanged(snapshot, t1, scratch, Latest);
            if (changed != null)
            {
                sendFailing = !TrySendUpdate(client, changed, fileLogger, logFailure: !sendFailing);
                Volatile.Write(ref Latest, changed);
                diagnostics?.CountPush();
            }
            timerResolution.Observe(t1, changed != null);
            diagnostics?.Record(t0, t1);

            Thread.Sleep(1);
        }

        timerResolution.Release();
    }

    private static bool TrySendUpdate(VrcftSandboxClient client, ReplyUpdatePacket packet, ILogger fileLogger, bool logFailure)
    {
        try
        {
            client.SendData(packet);
            return true;
        }
        catch (SocketException e)
        {
            if (logFailure)
            {
                LogUpdateSendsFailing(fileLogger, e.SocketErrorCode);
            }
            return false;
        }
    }

    private static void TeardownModule(ExtTrackingModule trackingModule, ModuleAssembly moduleAssembly, VrcftSandboxClient client)
    {
        LogReceivedTeardownPacket(Logger);
        moduleAssembly.UpdateCts?.Cancel();
        UpdateThread?.Join(TimeSpan.FromSeconds(2));
        trackingModule.Teardown();

        LogCancelledUpdateThreads(Logger);

        client.SendData(new ReplyTeardownPacket());

        LogSentTeardownAck(Logger);

        WaitForPackets = false;
        Wakeup.Set();
    }

    private static void QueueLatestUpdate()
    {
        ReplyUpdatePacket? latest = Volatile.Read(ref Latest);
        if (latest != null)
        {
            PacketsToSend.Enqueue(latest);
            Wakeup.Set();
        }
        if (ImageStreamEnabled)
        {
            QueueImageFrame(UnifiedTracking.EyeImageData, ImageFrameUpdatePacket.EyeKind, EyeFrameTimer);
            QueueImageFrame(UnifiedTracking.LipImageData, ImageFrameUpdatePacket.LipKind, LipFrameTimer);
        }
    }

    private static void QueueImageFrame(Core.Types.Image image, byte kind, Stopwatch timer)
    {
        if (timer.ElapsedMilliseconds < ImageFrameIntervalMs)
        {
            return;
        }

        byte[]? data = image?.ImageData;
        (int x, int y) = image?.ImageSize ?? default;
        if (image is not { SupportsImage: true } || data == null ||
            x < 1 || y < 1 ||
            x > ImageFrameUpdatePacket.MaxDimension || y > ImageFrameUpdatePacket.MaxDimension ||
            data.Length != x * y * 4)
        {
            return;
        }

        timer.Restart();
        PacketsToSend.Enqueue(new ImageFrameUpdatePacket(kind, x, y, (byte[])data.Clone()));
        Wakeup.Set();
    }

    private static void ApplyStatus(EventStatusUpdatePacket status, ExtTrackingModule trackingModule)
    {
        trackingModule.Status = status.ModuleState;
        trackingModule.ModuleInformation.UsingEye = status.UsingEye;
        trackingModule.ModuleInformation.UsingExpression = status.UsingExpression;
    }

    private static void SetVerbose(bool verbose)
    {
        Gate.Set(verbose || BuildInfo.VerboseForced);
        LogFile?.WriteRaw($"verbose={Gate.Verbose} minimum={Gate.Minimum}");
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{ModuleName} does not properly implement ExtTrackingModule. Skipping.")]
    private static partial void LogDoesNotProperlyImplementExtTrackingModule(ILogger logger, string moduleName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tracking updates to the host are failing ({Error})")]
    private static partial void LogUpdateSendsFailing(ILogger logger, SocketError error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Received Teardown packet")]
    private static partial void LogReceivedTeardownPacket(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cancelled Update Threads")]
    private static partial void LogCancelledUpdateThreads(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sent teardown ACK")]
    private static partial void LogSentTeardownAck(ILogger logger);
}
