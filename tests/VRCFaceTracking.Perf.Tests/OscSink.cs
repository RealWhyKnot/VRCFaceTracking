using System.Net;
using System.Net.Sockets;

namespace VRCFaceTracking.Perf.Tests;

internal sealed class OscSink : IDisposable
{
    public readonly Socket Socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private readonly Thread? _drain;
    private long _datagrams;

    public OscSink(bool drain)
    {
        Socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        Socket.ReceiveBufferSize = 4 * 1024 * 1024;
        if (!drain)
        {
            return;
        }

        Socket.ReceiveTimeout = 250;
        _drain = new Thread(() =>
        {
            byte[] buffer = new byte[65536];
            while (true)
            {
                try
                {
                    Socket.Receive(buffer);
                    Interlocked.Increment(ref _datagrams);
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut)
                {
                    continue;
                }
                catch (Exception e) when (e is SocketException or ObjectDisposedException)
                {
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "OSC Sink",
        };
        _drain.Start();
    }

    public int Port => ((IPEndPoint)Socket.LocalEndPoint!).Port;

    public long Datagrams => Interlocked.Read(ref _datagrams);

    public void Dispose()
    {
        Socket.Dispose();
        _drain?.Join(1000);
    }
}
