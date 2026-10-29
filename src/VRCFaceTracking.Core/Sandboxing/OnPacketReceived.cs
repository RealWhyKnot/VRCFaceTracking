using VRCFaceTracking.Core.Sandboxing.Ipc;

namespace VRCFaceTracking.Core.Sandboxing;

public delegate void OnPacketReceived(in IpcPacket packet, in int port);
