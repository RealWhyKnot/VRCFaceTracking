using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using VRCFaceTracking.Core.Sandboxing;

namespace VRCFaceTracking.Core.Tests;

public sealed class UdpFullDuplexTests
{
    private sealed class Recorder : UdpFullDuplex
    {
        public readonly ManualResetEventSlim Received = new();
        public Thread? HandlerThread;

        public Recorder() : base(0)
        {
            BoundPort = ((IPEndPoint)ReceivingUdpClient.Client.LocalEndPoint!).Port;
        }

        public int BoundPort
        {
            get;
        }

        public override void OnBytesReceived(in byte[] data, in IPEndPoint endpoint)
        {
            HandlerThread = Thread.CurrentThread;
            Received.Set();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                Received.Dispose();
            }
        }
    }

    private static void Send(int port, byte value)
    {
        using var sender = new UdpClient();
        sender.Send([value], 1, new IPEndPoint(IPAddress.Loopback, port));
    }

    [Fact]
    public void ReceiveRunsOnDedicatedThread()
    {
        using var duplex = new Recorder();

        Send(duplex.BoundPort, 1);

        Assert.True(duplex.Received.Wait(TimeSpan.FromSeconds(5)));
        Thread thread = duplex.HandlerThread!;
        Assert.Equal("IPC Receive", thread.Name);
        Assert.True(thread.IsBackground);
        Assert.False(thread.IsThreadPoolThread);
    }

    [Fact]
    public async Task DisposeUnblocksReceiveAndEndsThread()
    {
        using var duplex = new Recorder();
        Send(duplex.BoundPort, 1);
        Assert.True(duplex.Received.Wait(TimeSpan.FromSeconds(5)));
        Thread thread = duplex.HandlerThread!;

        long disposeMs = await Task.Run(() =>
        {
            var elapsed = Stopwatch.StartNew();
            duplex.Dispose();
            return elapsed.ElapsedMilliseconds;
        }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(thread.IsAlive);
        if (OperatingSystem.IsWindows())
        {
            Assert.True(disposeMs < UdpFullDuplex.ReceiveTimeoutMs / 2, $"Dispose took {disposeMs} ms");
        }
    }
}
