using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using VRCFaceTracking.Core.OSC.Query;
using VRCFaceTracking.Core.OSC.Query.mDNS;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Tests;

public class OscQueryTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FastPoll = TimeSpan.FromMilliseconds(5);
    private static readonly IPEndPoint Endpoint = new(IPAddress.Loopback, 1);

    [Fact]
    public async Task PollAvatarId_DispatchesEachNewIdOnce()
    {
        var replies = new Queue<Func<string?>>(new Func<string?>[]
        {
            () => "avtr_a",
            () => "avtr_a",
            () => throw new HttpRequestException("refused"),
            () => "avtr_a",
            () => null,
            () => "avtr_b",
        });
        var drained = new TaskCompletionSource();
        var dispatched = new List<string>();
        string? loaded = null;
        using var cts = new CancellationTokenSource();

        var poll = OscQueryService.PollAvatarId(
            () => Endpoint,
            (_, _) =>
            {
                if (replies.Count == 0)
                {
                    drained.TrySetResult();
                    return Task.FromResult<string?>("avtr_b");
                }

                return Task.FromResult(replies.Dequeue()());
            },
            () => loaded,
            id =>
            {
                dispatched.Add(id);
                loaded = id;
            },
            FastPoll,
            NullLogger.Instance,
            cts.Token);

        await drained.Task.WaitAsync(Timeout);
        cts.Cancel();
        await poll.WaitAsync(Timeout);

        Assert.Equal(new[] { "avtr_a", "avtr_b" }, dispatched);
    }

    [Fact]
    public async Task PollAvatarId_FetchesNothingUntilTheEndpointIsKnown()
    {
        IPEndPoint? known = null;
        var reads = 0;
        var readWhileUnknown = new TaskCompletionSource();
        var fetched = new TaskCompletionSource<IPEndPoint>();
        var dispatched = new List<string>();
        string? loaded = null;
        using var cts = new CancellationTokenSource();

        var poll = OscQueryService.PollAvatarId(
            () =>
            {
                if (Interlocked.Increment(ref reads) == 3)
                {
                    readWhileUnknown.TrySetResult();
                }

                return Volatile.Read(ref known);
            },
            (target, _) =>
            {
                fetched.TrySetResult(target);
                return Task.FromResult<string?>("avtr_a");
            },
            () => loaded,
            id =>
            {
                dispatched.Add(id);
                loaded = id;
            },
            FastPoll,
            NullLogger.Instance,
            cts.Token);

        await readWhileUnknown.Task.WaitAsync(Timeout);
        Assert.False(fetched.Task.IsCompleted);

        Volatile.Write(ref known, Endpoint);
        Assert.Same(Endpoint, await fetched.Task.WaitAsync(Timeout));
        cts.Cancel();
        await poll.WaitAsync(Timeout);

        Assert.Equal(new[] { "avtr_a" }, dispatched);
    }

    [Fact]
    public async Task PollAvatarId_RetriesUntilTheIdIsLoaded()
    {
        var fetches = 0;
        var settled = new TaskCompletionSource();
        var dispatched = new List<string>();
        string? loaded = null;
        using var cts = new CancellationTokenSource();

        var poll = OscQueryService.PollAvatarId(
            () => Endpoint,
            (_, _) =>
            {
                if (Interlocked.Increment(ref fetches) == 6)
                {
                    settled.TrySetResult();
                }

                return Task.FromResult<string?>("avtr_a");
            },
            () => loaded,
            id =>
            {
                dispatched.Add(id);
                if (dispatched.Count == 3)
                {
                    loaded = id;
                }
            },
            FastPoll,
            NullLogger.Instance,
            cts.Token);

        await settled.Task.WaitAsync(Timeout);
        cts.Cancel();
        await poll.WaitAsync(Timeout);

        Assert.Equal(new[] { "avtr_a", "avtr_a", "avtr_a" }, dispatched);
    }

    [Fact]
    public async Task GetAvatarId_ReadsTheValueOfAvatarChange()
    {
        const string id = "avtr_00000000-0000-0000-0000-000000000001";
        var port = Utils.GetRandomFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var requestedPath = ServeOnce(listener,
            "{\"DESCRIPTION\":\"Avatar ID, updated whenever the user switches into a valid avatar.\",\"FULL_PATH\":\"/avatar/change\",\"ACCESS\":3,\"TYPE\":\"s\",\"VALUE\":[\"" + id + "\"]}");
        var parser = new OscQueryConfigParser(
            NullLogger<OscQueryConfigParser>.Instance,
            new AvatarConfigParser(NullLogger<AvatarConfigParser>.Instance),
            null!);
        using var cts = new CancellationTokenSource(Timeout);

        Assert.Equal(id, await parser.GetAvatarId(new IPEndPoint(IPAddress.Loopback, port), cts.Token));
        Assert.Equal("/avatar/change", await requestedPath.WaitAsync(Timeout));
    }

    [Fact]
    public async Task HttpHandler_Root_AdvertisesNoAddresses()
    {
        var port = Utils.GetRandomFreePort();
        using var handler = new HttpHandler(null!, NullLogger<HttpHandler>.Instance);
        handler.BindTo($"http://127.0.0.1:{port}/", 9);
        using var client = new HttpClient { Timeout = Timeout };

        var root = JsonConvert.DeserializeObject<OscQueryNode>(await client.GetStringAsync($"http://127.0.0.1:{port}/"));

        Assert.Equal("/", root!.FullPath);
        Assert.Null(root.Contents);
    }

    [Fact]
    public async Task AvatarReader_ReadsAnAvatarWithAUnicodeName()
    {
        const string id = "avtr_00000000-0000-0000-0000-000000000002";
        const string name = "ネコ Jürgen ✨";
        var change = "{\"FULL_PATH\":\"/avatar/change\",\"ACCESS\":3,\"TYPE\":\"s\",\"VALUE\":[\"" + id + "\"]}";
        var parameters = "{\"FULL_PATH\":\"/avatar/parameters\",\"ACCESS\":0,\"CONTENTS\":{\"FT\":{\"FULL_PATH\":\"/avatar/parameters/FT\",\"ACCESS\":0,\"CONTENTS\":{\"v2\":{\"FULL_PATH\":\"/avatar/parameters/FT/v2\",\"ACCESS\":0,\"CONTENTS\":{\"JawOpen\":{\"FULL_PATH\":\"/avatar/parameters/FT/v2/JawOpen\",\"ACCESS\":3,\"TYPE\":\"f\",\"VALUE\":[0.0]}}}}}}}";
        var nameNode = "{\"FULL_PATH\":\"/avatar/name\",\"ACCESS\":1,\"TYPE\":\"s\",\"VALUE\":[\"" + name + "\"]}";
        var bodies = new Dictionary<string, string>
        {
            ["/avatar"] = "{\"FULL_PATH\":\"/avatar\",\"ACCESS\":0,\"CONTENTS\":{\"change\":" + change + ",\"name\":" + nameNode + ",\"parameters\":" + parameters + "}}",
            ["/avatar/change"] = change,
            ["/avatar/parameters"] = parameters,
        };
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var cts = new CancellationTokenSource(Timeout);
        var server = ServeWithCharCountLength(listener, bodies, cts.Token);
        try
        {
            using var http = new HttpClient { Timeout = Timeout };
            var avatarUri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/avatar");

            await Assert.ThrowsAnyAsync<JsonException>(async () => JsonConvert.DeserializeObject<OscQueryNode>(await http.GetStringAsync(avatarUri)));

            var info = new OscQueryAvatarInfo(await OscQueryAvatarReader.ReadAsync(http, avatarUri));

            Assert.Equal(id, info.Id);
            Assert.Equal("/avatar/parameters/FT/v2/JawOpen", Assert.Single(info.Parameters).Address);
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            await server.WaitAsync(Timeout);
        }
    }

    private static async Task ServeWithCharCountLength(TcpListener listener, IReadOnlyDictionary<string, string> bodies, CancellationToken ct)
    {
        var connections = new List<Task>();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                connections.Add(Task.Run(async () =>
                {
                    using (client)
                    {
                        var stream = client.GetStream();
                        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                        try
                        {
                            while (await reader.ReadLineAsync(ct) is { Length: > 0 } requestLine)
                            {
                                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(ct)))
                                {
                                }

                                var head = bodies.TryGetValue(requestLine.Split(' ')[1], out var body)
                                    ? $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\n\r\n"
                                    : "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n";
                                await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
                                if (body != null)
                                {
                                    await stream.WriteAsync(Encoding.UTF8.GetBytes(body), ct);
                                }
                            }
                        }
                        catch (Exception e) when (e is OperationCanceledException or IOException)
                        {
                        }
                    }
                }, CancellationToken.None));
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        await Task.WhenAll(connections);
    }

    private static async Task<string> ServeOnce(HttpListener listener, string body)
    {
        var context = await listener.GetContextAsync();
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
        return context.Request.Url!.AbsolutePath;
    }
}
