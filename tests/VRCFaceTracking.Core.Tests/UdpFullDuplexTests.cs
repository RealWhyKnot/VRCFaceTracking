using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using VRCFaceTracking.Core.Sandboxing;

namespace VRCFaceTracking.Core.Tests;

public class UdpFullDuplexTests
{
    private sealed class Recorder : UdpFullDuplex
    {
        public readonly ManualResetEventSlim Received = new();
        public Thread? HandlerThread;
        public int Errors;

        public Recorder() : base(0)
        {
            BoundPort = ((IPEndPoint)_receivingUdpClient.Client.LocalEndPoint!).Port;
        }

        public int BoundPort
        {
            get;
        }

        public override void OnBytesReceived(in byte[] data, in IPEndPoint endpoint)
        {
            HandlerThread = Thread.CurrentThread;
            if (data[0] == 0xFF)
            {
                throw new InvalidOperationException("handler failure");
            }
            Received.Set();
        }

        protected override void OnReceiveError(Exception ex) => Interlocked.Increment(ref Errors);
    }

    private static void Send(int port, byte value)
    {
        using var sender = new UdpClient();
        sender.Send(new[] { value }, 1, new IPEndPoint(IPAddress.Loopback, port));
    }

    [Fact]
    public void Receive_RunsOnDedicatedThread_AndSurvivesHandlerErrors()
    {
        using var duplex = new Recorder();

        Send(duplex.BoundPort, 0xFF);
        Send(duplex.BoundPort, 1);

        Assert.True(duplex.Received.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Volatile.Read(ref duplex.Errors));
        var thread = duplex.HandlerThread!;
        Assert.Equal("IPC Receive", thread.Name);
        Assert.True(thread.IsBackground);
        Assert.False(thread.IsThreadPoolThread);
    }

    [Fact]
    public async Task Dispose_UnblocksReceive_AndEndsThread()
    {
        var duplex = new Recorder();
        Send(duplex.BoundPort, 1);
        Assert.True(duplex.Received.Wait(TimeSpan.FromSeconds(5)));
        var thread = duplex.HandlerThread!;

        var disposeMs = await Task.Run(() =>
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
