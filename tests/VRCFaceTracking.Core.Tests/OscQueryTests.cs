using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using VRCFaceTracking.Core.Osc.Query;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Tests;

public sealed class OscQueryTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FastPoll = TimeSpan.FromMilliseconds(5);
    private static readonly IPEndPoint Endpoint = new(IPAddress.Loopback, 1);

    [Fact]
    public async Task PollAvatarIdDispatchesEachNewIdOnce()
    {
        var replies = new Queue<Func<string?>>(
        [
            () => "avtr_a",
            () => "avtr_a",
            () => throw new HttpRequestException("refused"),
            () => "avtr_a",
            () => null,
            () => "avtr_b",
        ]);
        var drained = new TaskCompletionSource();
        var dispatched = new List<string>();
        string? loaded = null;
        using var cts = new CancellationTokenSource();

        Task poll = OscQueryService.PollAvatarIdAsync(
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

        await drained.Task.WaitAsync(Timeout, cts.Token);
        await cts.CancelAsync();
        await poll.WaitAsync(Timeout, CancellationToken.None);

        Assert.Equal(["avtr_a", "avtr_b"], dispatched, StringComparer.Ordinal);
    }

    [Fact]
    public async Task PollAvatarIdFetchesNothingUntilTheEndpointIsKnown()
    {
        IPEndPoint? known = null;
        int reads = 0;
        var readWhileUnknown = new TaskCompletionSource();
        var fetched = new TaskCompletionSource<IPEndPoint>();
        var dispatched = new List<string>();
        string? loaded = null;
        using var cts = new CancellationTokenSource();

        Task poll = OscQueryService.PollAvatarIdAsync(
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

        await readWhileUnknown.Task.WaitAsync(Timeout, cts.Token);
        Assert.False(fetched.Task.IsCompleted);

        Volatile.Write(ref known, Endpoint);
        Assert.Same(Endpoint, await fetched.Task.WaitAsync(Timeout, cts.Token));
        await cts.CancelAsync();
        await poll.WaitAsync(Timeout, CancellationToken.None);

        Assert.Equal(["avtr_a"], dispatched, StringComparer.Ordinal);
    }

    [Fact]
    public async Task PollAvatarIdRetriesUntilTheIdIsLoaded()
    {
        int fetches = 0;
        var settled = new TaskCompletionSource();
        var dispatched = new List<string>();
        string? loaded = null;
        using var cts = new CancellationTokenSource();

        Task poll = OscQueryService.PollAvatarIdAsync(
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

        await settled.Task.WaitAsync(Timeout, cts.Token);
        await cts.CancelAsync();
        await poll.WaitAsync(Timeout, CancellationToken.None);

        Assert.Equal(["avtr_a", "avtr_a", "avtr_a"], dispatched, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetAvatarIdReadsTheValueOfAvatarChange()
    {
        const string Id = "avtr_00000000-0000-0000-0000-000000000001";
        int port = Utils.GetRandomFreePort();
        using var cts = new CancellationTokenSource(Timeout);
        Task<string> requestedPath = ServeOnceAsync(port,
            "{\"DESCRIPTION\":\"Avatar ID, updated whenever the user switches into a valid avatar.\",\"FULL_PATH\":\"/avatar/change\",\"ACCESS\":3,\"TYPE\":\"s\",\"VALUE\":[\"" + Id + "\"]}", cts.Token);
        using var parser = new OscQueryConfigParser(
            NullLogger<OscQueryConfigParser>.Instance,
            new AvatarConfigParser(NullLogger<AvatarConfigParser>.Instance),
            null!);

        Assert.Equal(Id, await parser.GetAvatarIdAsync(new IPEndPoint(IPAddress.Loopback, port), cts.Token));
        Assert.Equal("/avatar/change", await requestedPath.WaitAsync(Timeout, cts.Token));
    }

    [Fact]
    public async Task HttpHandlerRootAdvertisesNoAddresses()
    {
        int port = Utils.GetRandomFreePort();
        using var handler = new HttpHandler(null!, NullLogger<HttpHandler>.Instance);
        handler.BindTo(new Uri($"http://127.0.0.1:{port}/"), 9);
        using var client = new HttpClient { Timeout = Timeout };

        OscQueryNode? root = JsonConvert.DeserializeObject<OscQueryNode>(await client.GetStringAsync(new Uri($"http://127.0.0.1:{port}/")));

        Assert.Equal("/", root!.FullPath);
        Assert.Null(root.Contents);
    }

    [Fact]
    public async Task AvatarReaderReadsAnAvatarWithAUnicodeName()
    {
        const string Id = "avtr_00000000-0000-0000-0000-000000000002";
        const string Name = "ネコ Jürgen ✨";
        string change = "{\"FULL_PATH\":\"/avatar/change\",\"ACCESS\":3,\"TYPE\":\"s\",\"VALUE\":[\"" + Id + "\"]}";
        string parameters = "{\"FULL_PATH\":\"/avatar/parameters\",\"ACCESS\":0,\"CONTENTS\":{\"FT\":{\"FULL_PATH\":\"/avatar/parameters/FT\",\"ACCESS\":0,\"CONTENTS\":{\"v2\":{\"FULL_PATH\":\"/avatar/parameters/FT/v2\",\"ACCESS\":0,\"CONTENTS\":{\"JawOpen\":{\"FULL_PATH\":\"/avatar/parameters/FT/v2/JawOpen\",\"ACCESS\":3,\"TYPE\":\"f\",\"VALUE\":[0.0]}}}}}}}";
        string nameNode = "{\"FULL_PATH\":\"/avatar/name\",\"ACCESS\":1,\"TYPE\":\"s\",\"VALUE\":[\"" + Name + "\"]}";
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/avatar"] = "{\"FULL_PATH\":\"/avatar\",\"ACCESS\":0,\"CONTENTS\":{\"change\":" + change + ",\"name\":" + nameNode + ",\"parameters\":" + parameters + "}}",
            ["/avatar/change"] = change,
            ["/avatar/parameters"] = parameters,
            ["/avatar/name"] = nameNode,
        };
        int port = Utils.GetRandomFreePort();
        using var cts = new CancellationTokenSource(Timeout);
        Task server = ServeWithCharCountLengthAsync(port, bodies, cts.Token);
        try
        {
            using var http = new HttpClient { Timeout = Timeout };
            var avatarUri = new Uri($"http://127.0.0.1:{port}/avatar");

            await Assert.ThrowsAnyAsync<JsonException>(async () => JsonConvert.DeserializeObject<OscQueryNode>(await http.GetStringAsync(avatarUri, cts.Token)));

            var info = new OscQueryAvatarInfo(await OscQueryAvatarReader.ReadAsync(http, avatarUri, cts.Token));

            Assert.Equal(Id, info.Id);
            Assert.Equal("/avatar/parameters/FT/v2/JawOpen", Assert.Single(info.Parameters).Address);
            Assert.False(info.FullFaceTracking);
            Assert.Equal(OscQueryAvatarInfo.UnknownName, info.Name);
        }
        finally
        {
            await cts.CancelAsync();
            await server.WaitAsync(Timeout, CancellationToken.None);
        }
    }

    [Fact]
    public async Task AvatarReaderReadsTheFaceTrackingFlagWhenTheAvatarNodeFailsToParse()
    {
        const string Id = "avtr_00000000-0000-0000-0000-000000000003";
        string change = "{\"FULL_PATH\":\"/avatar/change\",\"ACCESS\":3,\"TYPE\":\"s\",\"VALUE\":[\"" + Id + "\"]}";
        string parameters = "{\"FULL_PATH\":\"/avatar/parameters\",\"ACCESS\":0,\"CONTENTS\":{}}";
        string nameNode = "{\"FULL_PATH\":\"/avatar/name\",\"ACCESS\":1,\"TYPE\":\"s\",\"VALUE\":[\"ネコ\"]}";
        string fullFaceTracking = "{\"FULL_PATH\":\"/avatar/avatarfullfacetracking\",\"ACCESS\":1,\"TYPE\":\"T\",\"VALUE\":[true]}";
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/avatar"] = "{\"FULL_PATH\":\"/avatar\",\"ACCESS\":0,\"CONTENTS\":{\"change\":" + change + ",\"name\":" + nameNode + ",\"parameters\":" + parameters + ",\"avatarfullfacetracking\":" + fullFaceTracking + "}}",
            ["/avatar/change"] = change,
            ["/avatar/parameters"] = parameters,
            ["/avatar/avatarfullfacetracking"] = fullFaceTracking,
        };
        int port = Utils.GetRandomFreePort();
        using var cts = new CancellationTokenSource(Timeout);
        Task server = ServeWithCharCountLengthAsync(port, bodies, cts.Token);
        try
        {
            using var http = new HttpClient { Timeout = Timeout };
            var avatarUri = new Uri($"http://127.0.0.1:{port}/avatar");

            var info = new OscQueryAvatarInfo(await OscQueryAvatarReader.ReadAsync(http, avatarUri, cts.Token));

            Assert.Equal(Id, info.Id);
            Assert.True(info.FullFaceTracking);
        }
        finally
        {
            await cts.CancelAsync();
            await server.WaitAsync(Timeout, CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(",\"avatarfullfacetracking\":{\"DESCRIPTION\":\"Whether the current avatar has hardware face tracking enabled (face tracking with all tracking keys).\",\"FULL_PATH\":\"/avatar/avatarfullfacetracking\",\"ACCESS\":1,\"TYPE\":\"T\",\"VALUE\":[true]}", true)]
    [InlineData(",\"avatarfullfacetracking\":{\"DESCRIPTION\":\"Whether the current avatar has hardware face tracking enabled (face tracking with all tracking keys).\",\"FULL_PATH\":\"/avatar/avatarfullfacetracking\",\"ACCESS\":1,\"TYPE\":\"T\",\"VALUE\":[false]}", false)]
    [InlineData(",\"avatarfullfacetracking\":{\"FULL_PATH\":\"/avatar/avatarfullfacetracking\",\"ACCESS\":1,\"TYPE\":\"T\"}", false)]
    [InlineData("", false)]
    public void AvatarInfoReadsTheFaceTrackingFlag(string flagNode, bool expected)
    {
        string json = "{\"FULL_PATH\":\"/avatar\",\"ACCESS\":0,\"CONTENTS\":{"
            + "\"change\":{\"FULL_PATH\":\"/avatar/change\",\"ACCESS\":3,\"TYPE\":\"s\",\"VALUE\":[\"avtr_x\"]},"
            + "\"parameters\":{\"FULL_PATH\":\"/avatar/parameters\",\"ACCESS\":0,\"CONTENTS\":{}}"
            + flagNode + "}}";

        var info = new OscQueryAvatarInfo(JsonConvert.DeserializeObject<OscQueryNode>(json)!);

        Assert.Equal(expected, info.FullFaceTracking);
    }

    [Fact]
    public async Task AvatarReaderReadsTheNameNodeWhenTheAvatarNodeFailsToParse()
    {
        const string Id = "avtr_00000000-0000-0000-0000-000000000004";
        string change = "{\"FULL_PATH\":\"/avatar/change\",\"ACCESS\":3,\"TYPE\":\"s\",\"VALUE\":[\"" + Id + "\"]}";
        string parameters = "{\"FULL_PATH\":\"/avatar/parameters\",\"ACCESS\":0,\"CONTENTS\":{}}";
        string nameNode = "{\"FULL_PATH\":\"/avatar/name\",\"ACCESS\":1,\"TYPE\":\"s\",\"VALUE\":[\"Kitty\"]}";
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/avatar"] = "{\"FULL_PATH\":\"/avatar\",\"ACCESS\":0,\"CONTENTS\":{\"change\":" + change + ",\"name\":" + nameNode + ",\"parameters\":" + parameters + ",\"note\":{\"FULL_PATH\":\"/avatar/note\",\"VALUE\":[\"ネコ\"]}}}",
            ["/avatar/change"] = change,
            ["/avatar/parameters"] = parameters,
            ["/avatar/name"] = nameNode,
        };
        int port = Utils.GetRandomFreePort();
        using var cts = new CancellationTokenSource(Timeout);
        Task server = ServeWithCharCountLengthAsync(port, bodies, cts.Token);
        try
        {
            using var http = new HttpClient { Timeout = Timeout };

            var info = new OscQueryAvatarInfo(await OscQueryAvatarReader.ReadAsync(http, new Uri($"http://127.0.0.1:{port}/avatar"), cts.Token));

            Assert.Equal(Id, info.Id);
            Assert.Equal("Kitty", info.Name);
        }
        finally
        {
            await cts.CancelAsync();
            await server.WaitAsync(Timeout, CancellationToken.None);
        }
    }

    private const string ChangeNode = "\"change\":{\"FULL_PATH\":\"/avatar/change\",\"ACCESS\":3,\"TYPE\":\"s\",\"VALUE\":[\"avtr_x\"]}";
    private const string GoodParameter = "\"Good\":{\"FULL_PATH\":\"/avatar/parameters/Good\",\"ACCESS\":3,\"TYPE\":\"f\",\"VALUE\":[0.0]}";

    private static OscQueryAvatarInfo AvatarInfo(string contents) =>
        new(JsonConvert.DeserializeObject<OscQueryNode>("{\"FULL_PATH\":\"/avatar\",\"ACCESS\":0,\"CONTENTS\":{" + contents + "}}")!);

    [Theory]
    [InlineData("\"Bad\":null")]
    [InlineData("\"Bad\":{\"FULL_PATH\":null,\"ACCESS\":3,\"TYPE\":\"f\"}")]
    [InlineData("\"Bad\":{\"ACCESS\":3,\"TYPE\":\"f\"}")]
    [InlineData("\"Bad\":{\"FULL_PATH\":\"/avatar/parameters/Bad\",\"ACCESS\":3,\"TYPE\":\"q\"}")]
    [InlineData("\"Bad\":{\"FULL_PATH\":\"/avatar/parameters/Bad\",\"ACCESS\":3,\"TYPE\":\"\"}")]
    [InlineData("\"Bad\":{\"FULL_PATH\":\"/avatar/parameters/Bad\",\"ACCESS\":3,\"TYPE\":null}")]
    [InlineData("\"Bad\":{\"FULL_PATH\":\"/avatar/parameters/Bad\",\"ACCESS\":3}")]
    [InlineData("\"Bad\":{\"FULL_PATH\":\"/avatar/parameters/Bad\",\"CONTENTS\":{\"Inner\":null}}")]
    [InlineData("\"Bad\":{\"FULL_PATH\":\"/avatar/parameters/Bad\",\"CONTENTS\":{}}")]
    public void AvatarInfoSkipsMalformedParameterNodes(string badNode)
    {
        OscQueryAvatarInfo info = AvatarInfo(ChangeNode + ",\"parameters\":{\"FULL_PATH\":\"/avatar/parameters\",\"ACCESS\":0,\"CONTENTS\":{" + badNode + "," + GoodParameter + "}}");

        Assert.Equal("avtr_x", info.Id);
        Assert.Equal("/avatar/parameters/Good", Assert.Single(info.Parameters).Address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"change\":null,\"parameters\":null")]
    [InlineData("\"change\":{\"FULL_PATH\":\"/avatar/change\",\"VALUE\":[5]},\"parameters\":{\"FULL_PATH\":\"/avatar/parameters\"}")]
    [InlineData("\"change\":{\"FULL_PATH\":\"/avatar/change\",\"VALUE\":[]},\"parameters\":{\"FULL_PATH\":\"/avatar/parameters\",\"CONTENTS\":null}")]
    [InlineData("\"change\":{\"VALUE\":[null]},\"parameters\":{\"CONTENTS\":{}},\"name\":{\"VALUE\":[7]},\"avatarfullfacetracking\":{\"VALUE\":[\"yes\"]}")]
    public void AvatarInfoSurvivesMissingOrWrongTypedNodes(string contents)
    {
        OscQueryAvatarInfo info = AvatarInfo(contents);

        Assert.Equal("Unknown", info.Id);
        Assert.Equal(OscQueryAvatarInfo.UnknownName, info.Name);
        Assert.Empty(info.Parameters);
        Assert.False(info.FullFaceTracking);
    }

    [Theory]
    [InlineData("[\"ネコ Jürgen ✨\"]", "ネコ Jürgen ✨")]
    [InlineData("[\"\"]", OscQueryAvatarInfo.UnknownName)]
    [InlineData("[\"   \"]", OscQueryAvatarInfo.UnknownName)]
    [InlineData("[]", OscQueryAvatarInfo.UnknownName)]
    [InlineData("[false]", OscQueryAvatarInfo.UnknownName)]
    public void AvatarInfoReadsTheAvatarName(string value, string expected)
    {
        OscQueryAvatarInfo info = AvatarInfo(ChangeNode + ",\"parameters\":{\"FULL_PATH\":\"/avatar/parameters\",\"CONTENTS\":{}},\"name\":{\"FULL_PATH\":\"/avatar/name\",\"ACCESS\":1,\"TYPE\":\"s\",\"VALUE\":" + value + "}");

        Assert.Equal(expected, info.Name);
    }

    [Fact]
    public void DeeplyNestedTreeFailsAsBadJson()
    {
        var json = new StringBuilder();
        for (int i = 0; i < 5000; i++)
        {
            json.Append("{\"CONTENTS\":{\"a\":");
        }

        json.Append("{}");
        for (int i = 0; i < 5000; i++)
        {
            json.Append("}}");
        }

        Assert.ThrowsAny<JsonException>(() => JsonConvert.DeserializeObject<OscQueryNode>(json.ToString()));
    }

    private static async Task ServeWithCharCountLengthAsync(int port, Dictionary<string, string> bodies, CancellationToken ct)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var connections = new List<Task>();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(ct);
                connections.Add(Task.Run(async () =>
                {
                    using (client)
                    {
                        NetworkStream stream = client.GetStream();
                        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, 1024, leaveOpen: true);
                        try
                        {
                            while (await reader.ReadLineAsync(ct) is { Length: > 0 } requestLine)
                            {
                                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(ct)))
                                {
                                }

                                string head = bodies.TryGetValue(requestLine.Split(' ')[1], out string? body)
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
                            return;
                        }
                    }
                }, CancellationToken.None));
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            return;
        }
        finally
        {
            await Task.WhenAll(connections);
        }
    }

    private static async Task<string> ServeOnceAsync(int port, string body, CancellationToken cancellationToken)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        HttpListenerContext context = await listener.GetContextAsync().WaitAsync(cancellationToken);
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.KeepAlive = false;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();
        return context.Request.Url!.AbsolutePath;
    }
}
