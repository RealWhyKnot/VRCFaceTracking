using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
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
}
