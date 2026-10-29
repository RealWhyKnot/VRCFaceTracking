using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using VRCFaceTracking.Core.Sandboxing.Ipc;

namespace VRCFaceTracking.Core.Sandboxing;

public sealed class SimpleEventBus
{
    private readonly ConcurrentQueue<IpcPacket> _packetQueue = new();

    public int Count => _packetQueue.Count;

    public void Push(IpcPacket packet) => _packetQueue.Enqueue(packet);

    public bool TryPop([NotNullWhen(true)] out IpcPacket? packet) => _packetQueue.TryDequeue(out packet);
}
