using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Tests;

public class OscRecvServiceTests
{
    private sealed class FakeOscTarget : IOscTarget
    {
        public event PropertyChangedEventHandler PropertyChanged
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
        public string DestinationAddress { get; set; } = "127.0.0.1";
    }

    private sealed class NullSettings : ILocalSettingsService
    {
        public Task<T> ReadSettingAsync<T>(string key, T? defaultValue = default, bool forceLocal = false) => Task.FromResult(defaultValue!);
        public Task SaveSettingAsync<T>(string key, T value, bool forceLocal = false) => Task.CompletedTask;
        public Task Save(object target) => Task.CompletedTask;
        public Task Load(object target) => Task.CompletedTask;
        public Task FlushAsync() => Task.CompletedTask;
    }

    [Fact]
    public void UpdateTarget_Loopback_StaysOnLoopback()
    {
        using var service = new OscRecvService(NullLogger<OscRecvService>.Instance, new FakeOscTarget(), new NullSettings());

        var bound = service.UpdateTarget(new IPEndPoint(IPAddress.Loopback, 0));

        Assert.NotNull(bound);
        Assert.Equal(IPAddress.Loopback, bound.Address);
    }

    [Fact]
    public async Task UpdateTarget_OtherMachine_ReceivesOnTheLocalAddressThatReachesIt()
    {
        var target = new FakeOscTarget();
        using var service = new OscRecvService(NullLogger<OscRecvService>.Instance, target, new NullSettings());
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.OnMessagesReceived = _ => received.TrySetResult();
        await service.StartAsync(CancellationToken.None);

        try
        {
            var bound = service.UpdateTarget(new IPEndPoint(IPAddress.Parse("192.0.2.10"), 0));

            Assert.NotNull(bound);
            Assert.False(IPAddress.IsLoopback(bound.Address));
            Assert.True(target.IsConnected);

            using var sender = new UdpClient(AddressFamily.InterNetwork);
            sender.Send(new byte[] { 1, 2, 3, 4 }, 4, bound);
            Assert.Same(received.Task, await Task.WhenAny(received.Task, Task.Delay(2000)));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
}
