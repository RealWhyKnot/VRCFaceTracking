using VRCFaceTracking.Core.Sandboxing.Ipc;

namespace VRCFaceTracking.Core.Library;

public readonly record struct QueuedPacket(IpcPacket Packet, int DestinationPort);
