using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using VRCFaceTracking.Core.Sandboxing.IPC;

namespace VRCFaceTracking.Core.Sandboxing;

public class SimpleEventBus
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<IpcPacket> _packetQueue = new();
    /// <summary>
    /// Whether or not to bypass the queue
    /// </summary>
    public bool BypassQueue = false;

    public int Count => _packetQueue.Count;

    public void Push<T>(T packet) where T : IpcPacket
    {
        _packetQueue.Enqueue(packet);
    }

    public T Pop<T>() where T : IpcPacket
    {
        return _packetQueue.TryDequeue(out var packet) ? (T)packet : default;
    }

    public T Peek<T>() where T : IpcPacket
    {
        return _packetQueue.TryPeek(out var packet) ? (T)packet : default;
    }
}
