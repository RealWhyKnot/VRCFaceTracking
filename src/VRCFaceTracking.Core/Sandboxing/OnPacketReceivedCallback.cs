using VRCFaceTracking.Core.Sandboxing.Ipc;

namespace VRCFaceTracking.Core.Sandboxing;

public delegate void OnPacketReceivedCallback(in IpcPacket packet);
