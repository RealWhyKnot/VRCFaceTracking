using System.Net;
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
