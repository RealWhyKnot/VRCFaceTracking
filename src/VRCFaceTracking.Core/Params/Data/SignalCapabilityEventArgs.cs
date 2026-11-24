namespace VRCFaceTracking.Core.Params.Data;

public sealed class SignalCapabilityEventArgs : EventArgs
{
    public SignalCapabilityEventArgs(SignalCapabilitySnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public SignalCapabilitySnapshot Snapshot { get; }
}
