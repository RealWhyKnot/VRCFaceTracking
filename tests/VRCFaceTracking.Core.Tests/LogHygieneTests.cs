using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.Mdns;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Tests;

public sealed class LogHygieneTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly IPEndPoint Endpoint = new(IPAddress.Loopback, 1);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vrcft-tests-" + Guid.NewGuid().ToString("N"));

    public LogHygieneTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Touch(string name, DateTime created, int bytes)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetCreationTimeUtc(path, created);
        return path;
    }

    [Fact]
    public void SizeCapPrunesOldestFilesFirst()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 10; i++)
        {
            Touch(LogFileNames.Main(start.AddMinutes(i)), start.AddMinutes(i), 100);
        }

        int deleted = LogRetention.Prune(_dir, LogFileNames.MainPattern, keep: 20, maxTotalBytes: 450);

        Assert.Equal(6, deleted);
        string[] remaining = [.. Directory.GetFiles(_dir).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];
        Assert.Equal(4, remaining.Length);
        Assert.Equal(LogFileNames.Main(start.AddMinutes(6)), remaining[0]);
        Assert.Equal(LogFileNames.Main(start.AddMinutes(9)), remaining[^1]);
    }

    [Fact]
    public void SizeCapDeletesEverythingOlderThanTheFirstFileOverTheCap()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        string oldest = Touch(LogFileNames.Main(start), start, 50);
        string large = Touch(LogFileNames.Main(start.AddMinutes(1)), start.AddMinutes(1), 400);
        string newest = Touch(LogFileNames.Main(start.AddMinutes(2)), start.AddMinutes(2), 100);

        int deleted = LogRetention.Prune(_dir, LogFileNames.MainPattern, keep: 20, maxTotalBytes: 450);

        Assert.Equal(2, deleted);
        Assert.False(File.Exists(oldest));
        Assert.False(File.Exists(large));
        Assert.True(File.Exists(newest));
    }

    [Fact]
    public void CountLimitStillAppliesUnderTheSizeCap()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 25; i++)
        {
            Touch(LogFileNames.Main(start.AddMinutes(i)), start.AddMinutes(i), 10);
        }

        int deleted = LogRetention.Prune(_dir, LogFileNames.MainPattern, keep: 20);

        Assert.Equal(5, deleted);
        Assert.Equal(20, Directory.GetFiles(_dir).Length);
    }

    [Fact]
    public void SizeCapNeverDeletesTheNewestFile()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Touch(LogFileNames.Main(start), start, 500);
        string newest = Touch(LogFileNames.Main(start.AddMinutes(1)), start.AddMinutes(1), 500);

        LogRetention.Prune(_dir, LogFileNames.MainPattern, keep: 20, maxTotalBytes: 100);

        Assert.Equal([newest], Directory.GetFiles(_dir));
    }

    [Fact]
    public void SizeCapCountsMainAndModuleLogsTogether()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Touch(LogFileNames.Module("m", start), start, 300);
        Touch(LogFileNames.Main(start.AddMinutes(1)), start.AddMinutes(1), 300);
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "keep me");

        LogRetention.Prune(_dir, LogFileNames.MainPattern, keep: 20, maxTotalBytes: 400);

        Assert.Single(Directory.GetFiles(_dir, "vrcft_*.log"));
        Assert.True(File.Exists(Path.Combine(_dir, "notes.txt")));
    }

    [Fact]
    public void DefaultSizeCapIsOneHundredMegabytes() => Assert.Equal(100L * 1024 * 1024, LogRetention.DefaultMaxTotalBytes);

    [Fact]
    public void HeartbeatIsWrittenOnStartAndDeletedOnCleanStop()
    {
        var logger = new RecordingLogger();
        using var heartbeat = new SessionHeartbeat(logger, _dir, TimeSpan.FromHours(1));

        heartbeat.Start();

        Assert.True(File.Exists(heartbeat.FilePath));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);

        heartbeat.Stop();

        Assert.False(File.Exists(heartbeat.FilePath));
    }

    [Fact]
    public void LateHeartbeatTickAfterStopDoesNotRecreateTheFile()
    {
        using var heartbeat = new SessionHeartbeat(NullLogger.Instance, _dir, TimeSpan.FromHours(1));
        heartbeat.Start();
        heartbeat.Stop();

        heartbeat.Write();

        Assert.False(File.Exists(heartbeat.FilePath));
    }

    [Fact]
    public void LeftoverHeartbeatLogsOneWarningWithItsTimestamp()
    {
        var last = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        File.WriteAllText(Path.Combine(_dir, SessionHeartbeat.FileName), last.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + "\n999999999");
        var logger = new RecordingLogger();
        using var heartbeat = new SessionHeartbeat(logger, _dir, TimeSpan.FromHours(1));

        heartbeat.Start();

        (LogLevel level, string message, _) = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("ended unexpectedly", message, StringComparison.Ordinal);
        Assert.Contains("2026-03-04 05:06:07", message, StringComparison.Ordinal);
        Assert.Equal(last, heartbeat.PreviousSessionLastHeartbeat);
    }

    [Fact]
    public void CorruptHeartbeatStillWarnsOnce()
    {
        File.WriteAllText(Path.Combine(_dir, SessionHeartbeat.FileName), "garbage");
        var logger = new RecordingLogger();
        using var heartbeat = new SessionHeartbeat(logger, _dir, TimeSpan.FromHours(1));

        heartbeat.Start();
        heartbeat.Start();

        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Null(heartbeat.PreviousSessionLastHeartbeat);
    }

    [Fact]
    public void HeartbeatOfARunningOtherProcessIsNotACrash()
    {
        File.WriteAllText(Path.Combine(_dir, SessionHeartbeat.FileName), DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + "\n" + Environment.ProcessId);
        var logger = new RecordingLogger();
        using var heartbeat = new SessionHeartbeat(logger, _dir, TimeSpan.FromHours(1), processId: Environment.ProcessId + 1);

        heartbeat.Start();

        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task HeartbeatIsRefreshedPeriodically()
    {
        long now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks;
        using var heartbeat = new SessionHeartbeat(NullLogger.Instance, _dir, TimeSpan.FromMilliseconds(20), () => new DateTimeOffset(Interlocked.Add(ref now, TimeSpan.TicksPerSecond), TimeSpan.Zero));
        heartbeat.Start();
        string first = await File.ReadAllTextAsync(heartbeat.FilePath);

        using var cts = new CancellationTokenSource(Timeout);
        string current = first;
        while (string.Equals(current, first, StringComparison.Ordinal))
        {
            await Task.Delay(20, cts.Token);
            current = await ReadSharedAsync(heartbeat.FilePath);
        }

        Assert.NotEqual(first, current, StringComparer.Ordinal);
    }

    private static async Task<string> ReadSharedAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public void IpcRingKeepsOnlyTheMostRecentSamples()
    {
        var ring = new IpcLatencyRing();
        for (int i = 1; i <= 40; i++)
        {
            ring.Add(i, i * 2);
        }

        string[] parts = ring.Describe().Split(' ');

        Assert.Equal(IpcLatencyRing.Capacity, ring.Count);
        Assert.Equal(16, parts.Length);
        Assert.Equal("25/50", parts[0]);
        Assert.Equal("40/80", parts[^1]);
    }

    [Fact]
    public void IpcRingDescribesPartialFillInOrder()
    {
        var ring = new IpcLatencyRing();
        ring.Add(1, 2);
        ring.Add(3, 4);

        Assert.Equal("1/2 3/4", ring.Describe());
    }

    [Fact]
    public void SlowTransitLogsRecentContextAndNormalSamplesDoNot()
    {
        var logger = new RecordingLogger();
        var diagnostics = new IpcLatencyDiagnostics(logger);
        long frequency = System.Diagnostics.Stopwatch.Frequency;

        diagnostics.Record(Reply(frequency, moduleMs: 1, transitMs: 1), accepted: true);
        diagnostics.Record(Reply(frequency, moduleMs: 1, transitMs: 300), accepted: true);

        Assert.DoesNotContain(logger.Entries, entry => entry.Message.Contains("recent", StringComparison.Ordinal) && entry.Message.Contains("transit 1.0", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("SLOW transit", StringComparison.Ordinal) && entry.Message.Contains("recent module/transit ms:", StringComparison.Ordinal));
    }

    private static ReplyUpdatePacket Reply(long frequency, double moduleMs, double transitMs)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        long sent = now - (long)(transitMs * frequency / 1000.0);
        long sample = sent - (long)(moduleMs * frequency / 1000.0);
        return new ReplyUpdatePacket { SampleTicks = sample, ReplySentTicks = sent };
    }

    [Theory]
    [InlineData(265, 250, 0, false, ParameterSenderService.TickKind.IdleKeepalive)]
    [InlineData(262, 235, 0, false, ParameterSenderService.TickKind.IdleKeepalive)]
    [InlineData(25, 25, 0, false, ParameterSenderService.TickKind.IdleKeepalive)]
    [InlineData(30, 25, 4, true, ParameterSenderService.TickKind.Active)]
    [InlineData(110, 25, 4, true, ParameterSenderService.TickKind.Stall)]
    [InlineData(110, 25, 0, true, ParameterSenderService.TickKind.Stall)]
    [InlineData(600, 250, 0, false, ParameterSenderService.TickKind.Stall)]
    internal void IdleKeepaliveTicksAreNotStalls(double gapMs, int waitMs, int sent, bool dataChanged, ParameterSenderService.TickKind expected) =>
        Assert.Equal(expected, ParameterSenderService.ClassifyTick(gapMs, waitMs, sent, dataChanged));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 7)]
    [InlineData(4, 8)]
    [InlineData(50, 8)]
    public void PollBackoffStepsUpAndCaps(int failures, int skipped) =>
        Assert.Equal(skipped, OscQueryService.PollBackoffTicks(failures));

    [Fact]
    public async Task NotFoundPollsBackOffInsteadOfChurning()
    {
        const int TargetTicks = 40;
        int fetches = 0;
        int ticks = 0;
        var enoughTicks = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var logger = new RecordingLogger();

        Task poll = OscQueryService.PollAvatarIdAsync(
            () =>
            {
                if (Interlocked.Increment(ref ticks) >= TargetTicks)
                {
                    enoughTicks.TrySetResult();
                }

                return Endpoint;
            },
            (_, _) =>
            {
                Interlocked.Increment(ref fetches);
                throw new HttpRequestException("not found", inner: null, HttpStatusCode.NotFound);
            },
            () => null,
            _ => Task.FromResult(true),
            TimeSpan.FromMilliseconds(5),
            logger,
            cts.Token);

        await enoughTicks.Task.WaitAsync(Timeout, CancellationToken.None);
        await cts.CancelAsync();
        await poll.WaitAsync(Timeout, CancellationToken.None);

        Assert.InRange(fetches, 2, 12);
        Assert.Single(logger.Entries, entry => entry.Message.Contains("failed", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task PollEndsQuietlyWhenAFetchFailsDuringShutdown()
    {
        var fetching = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();

        Task poll = OscQueryService.PollAvatarIdAsync(
            () => Endpoint,
            async (_, _) =>
            {
                fetching.TrySetResult();
                await release.Task.WaitAsync(Timeout, CancellationToken.None);
                throw new HttpRequestException("aborted", inner: null, HttpStatusCode.NotFound);
            },
            () => null,
            _ => Task.FromResult(true),
            TimeSpan.FromMilliseconds(5),
            NullLogger.Instance,
            cts.Token);

        await fetching.Task.WaitAsync(Timeout, CancellationToken.None);
        await cts.CancelAsync();
        release.SetResult();
        await poll.WaitAsync(Timeout, CancellationToken.None);

        Assert.True(poll.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task PollBackoffResetsAfterTheConfigLoads()
    {
        int fetches = 0;
        var recovered = new TaskCompletionSource();
        using var cts = new CancellationTokenSource();

        Task poll = OscQueryService.PollAvatarIdAsync(
            () => Endpoint,
            (_, _) =>
            {
                int n = Interlocked.Increment(ref fetches);
                if (n <= 3)
                {
                    throw new HttpRequestException("not found", inner: null, HttpStatusCode.NotFound);
                }

                if (n >= 9)
                {
                    recovered.TrySetResult();
                }

                return Task.FromResult<string?>("avtr_a");
            },
            () => "avtr_a",
            _ => Task.FromResult(true),
            TimeSpan.FromMilliseconds(5),
            NullLogger.Instance,
            cts.Token);

        await recovered.Task.WaitAsync(Timeout, cts.Token);
        await cts.CancelAsync();
        await poll.WaitAsync(Timeout, CancellationToken.None);

        Assert.True(Volatile.Read(ref fetches) >= 9);
    }

    [Fact]
    public async Task AvatarConfigNotFoundLogsOneInformationLineAndDebugDetail()
    {
        int port = Utils.GetRandomFreePort();
        using var cts = new CancellationTokenSource(Timeout);
        Task server = ServeNotFoundAsync(port, requests: 3, cts.Token);
        var logger = new RecordingLogger<OscQueryConfigParser>();
        var dns = (MulticastDnsService)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MulticastDnsService));
        typeof(MulticastDnsService).GetProperty(nameof(MulticastDnsService.VrchatClientEndpoint))!
            .SetValue(dns, new IPEndPoint(IPAddress.Loopback, port));
        using var parser = new OscQueryConfigParser(logger, new AvatarConfigParser(NullLogger<AvatarConfigParser>.Instance), dns);

        Assert.Null(await parser.ParseAvatarAsync(cts.Token));
        Assert.Null(await parser.ParseAvatarAsync(cts.Token));

        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("not ready yet", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
        Assert.Equal(2, logger.Entries.Count(entry => entry.Level == LogLevel.Debug && entry.Exception is HttpRequestException));

        parser.NotifyAvatarChanged();
        Assert.Null(await parser.ParseAvatarAsync(cts.Token));

        Assert.Equal(2, logger.Entries.Count(entry => entry.Level == LogLevel.Information && entry.Message.Contains("not ready yet", StringComparison.Ordinal)));
        await server.WaitAsync(Timeout, CancellationToken.None);
    }

    private static async Task ServeNotFoundAsync(int port, int requests, CancellationToken cancellationToken)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        for (int i = 0; i < requests; i++)
        {
            HttpListenerContext context = await listener.GetContextAsync().WaitAsync(cancellationToken);
            byte[] bytes = Encoding.UTF8.GetBytes("{}");
            context.Response.StatusCode = 404;
            context.Response.ContentLength64 = bytes.Length;
            context.Response.KeepAlive = false;
            await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
            context.Response.Close();
        }
    }

    [Theory]
    [InlineData(SocketError.Interrupted, true, true)]
    [InlineData(SocketError.Interrupted, false, false)]
    [InlineData(SocketError.ConnectionReset, true, false)]
    [InlineData(SocketError.ConnectionReset, false, false)]
    public void OnlyInterruptedDuringTeardownIsQuiet(SocketError error, bool closing, bool quiet) =>
        Assert.Equal(quiet, UdpFullDuplex.IsExpectedShutdownError(error, closing));

    [Fact]
    public void ClosingTheSandboxServerLogsNothingAtWarning()
    {
        var logger = new RecordingLogger();
        using var factory = new SingleLoggerFactory(logger);
        var server = new VrcftSandboxServer(factory, []);

        server.Close();

        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    private sealed class SingleLoggerFactory : ILoggerFactory
    {
        private readonly ILogger _logger;

        public SingleLoggerFactory(ILogger logger)
        {
            _logger = logger;
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => _logger;

        public void Dispose()
        {
        }
    }
}
