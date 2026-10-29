using System.Buffers.Binary;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Osc;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Tests;

public sealed class OscRecvServiceTests
{
    private sealed class FakeOscTarget : IOscTarget
    {
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add
            {
            }
            remove
            {
            }
        }

        public bool IsConnected
        {
            get; set;
        }
        public int InPort
        {
            get; set;
        }
        public int OutPort
        {
            get; set;
        }
        public string? DestinationAddress { get; set; } = "127.0.0.1";
    }

    private sealed class NullSettings : ILocalSettingsService
    {
        public Task<T?> ReadSettingAsync<T>(string key, T? defaultValue, bool forceLocal, CancellationToken cancellationToken) => Task.FromResult(defaultValue);
        public Task SaveSettingAsync<T>(string key, T value, bool forceLocal, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public void UpdateTargetLoopbackStaysOnLoopback()
    {
        using var service = new OscRecvService(NullLogger<OscRecvService>.Instance, new FakeOscTarget(), new NullSettings());

        IPEndPoint? bound = service.UpdateTarget(new IPEndPoint(IPAddress.Loopback, 0));

        Assert.NotNull(bound);
        Assert.Equal(IPAddress.Loopback, bound.Address);
    }

    [Fact]
    public async Task UpdateTargetOtherMachineReceivesOnTheLocalAddressThatReachesIt()
    {
        var target = new FakeOscTarget();
        using var service = new OscRecvService(NullLogger<OscRecvService>.Instance, target, new NullSettings());
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.OnMessagesReceived = _ => received.TrySetResult();
        await service.StartAsync(CancellationToken.None);

        try
        {
            IPEndPoint? bound = service.UpdateTarget(new IPEndPoint(IPAddress.Parse("192.0.2.10"), 0));

            Assert.NotNull(bound);
            Assert.False(IPAddress.IsLoopback(bound.Address));
            Assert.True(target.IsConnected);

            using var sender = new UdpClient(AddressFamily.InterNetwork);
            await sender.SendAsync([1, 2, 3, 4], 4, bound);
            Assert.Same(received.Task, await Task.WhenAny(received.Task, Task.Delay(2000)));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static byte[] Message(string address, string value)
    {
        byte[] buffer = new byte[65536];
        int length = OscCodec.EncodeMessage(buffer, new OscMessage(address, [new OscValue { Type = OscValueType.String, StringValue = value }]));
        Assert.True(length > 0);
        return buffer[..length];
    }

    private static byte[] Bundle(params byte[][] elements)
    {
        var bundle = new List<byte>("#bundle\0"u8.ToArray()) { 0, 0, 0, 0, 0, 0, 0, 1 };
        byte[] size = new byte[4];
        foreach (byte[] element in elements)
        {
            BinaryPrimitives.WriteInt32BigEndian(size, element.Length);
            bundle.AddRange(size);
            bundle.AddRange(element);
        }

        return [.. bundle];
    }

    private static byte[] Nested(int depth, byte[] message)
    {
        byte[] datagram = message;
        for (int i = 0; i < depth; i++)
        {
            datagram = Bundle(datagram);
        }

        return datagram;
    }

    private static async Task<List<string>> DeliveredAvatarIdsAsync(params byte[][] datagrams)
    {
        using var service = new OscRecvService(NullLogger<OscRecvService>.Instance, new FakeOscTarget(), new NullSettings());
        service.HandleAddress("/avatar/change");
        var delivered = new List<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.OnMessageReceived = message =>
        {
            if (message.Value is "done")
            {
                done.TrySetResult();
            }
            else
            {
                delivered.Add(message.Value as string ?? "?");
            }
        };
        IPEndPoint? bound = service.UpdateTarget(new IPEndPoint(IPAddress.Loopback, 0));
        Assert.NotNull(bound);
        await service.StartAsync(CancellationToken.None);
        try
        {
            using var sender = new UdpClient(AddressFamily.InterNetwork);
            sender.Client.SendBufferSize = 65536;
            for (int i = 0; i < datagrams.Length; i++)
            {
                await sender.SendAsync(datagrams[i], bound);
                if (i % 25 == 24)
                {
                    await Task.Delay(5);
                }
            }

            byte[] sentinel = Message("/avatar/change", "done");
            for (int attempt = 0; attempt < 50 && !done.Task.IsCompleted; attempt++)
            {
                await sender.SendAsync(sentinel, bound);
                await Task.WhenAny(done.Task, Task.Delay(100));
            }

            Assert.True(done.Task.IsCompleted);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        return delivered;
    }

    [Fact]
    public async Task BundledMessagesAreDelivered()
    {
        List<string> delivered = await DeliveredAvatarIdsAsync(
            Bundle(Message("/other", "x"), Message("/avatar/change", "avtr_a"), Bundle(Message("/avatar/change", "avtr_b"))));

        Assert.Equal(["avtr_a", "avtr_b"], delivered);
    }

    [Fact]
    public async Task BundlesNestedPastTheLimitAreDropped()
    {
        List<string> delivered = await DeliveredAvatarIdsAsync(
            Nested(8, Message("/avatar/change", "eight")),
            Nested(9, Message("/avatar/change", "nine")),
            Nested(2000, Message("/avatar/change", "deep")));

        Assert.Equal(["eight"], delivered);
    }

    [Fact]
    public async Task FullSizeDatagramIsDelivered()
    {
        string id = "avtr_" + new string('a', 60000);

        List<string> delivered = await DeliveredAvatarIdsAsync(Message("/avatar/change", id));

        Assert.Equal(id, Assert.Single(delivered));
    }

    [Fact]
    public async Task GarbageDoesNotStopTheReceiveLoop()
    {
        var datagrams = new List<byte[]>();
        for (int seed = 0; seed < 300; seed++)
        {
            byte[] noise = Noise.Bytes(seed, 1 + Noise.Int(seed, 200));
            datagrams.Add(seed % 3 == 0 ? [.. Bundle(), .. noise] : noise);
        }

        datagrams.Add(Message("/avatar/change", "after"));

        List<string> delivered = await DeliveredAvatarIdsAsync([.. datagrams]);

        Assert.Equal(["after"], delivered);
    }
}
