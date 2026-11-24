using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Models;

namespace VRCFaceTracking.Core.Services;

public sealed class ExternalOscInputSettings
{
    public const int DefaultPort = 8888;

    [SavedSetting("ExternalOscInputEnabled", false)]
    public bool Enabled { get; set; }

    [SavedSetting("ExternalOscInputPort", DefaultPort)]
    public int Port { get; set; } = DefaultPort;

    public static ExternalOscPortCheck Check(int port, IOscTarget target)
    {
        bool conflicts = port == target.InPort || port == target.OutPort;
        return port is < OscTarget.MinPort or > OscTarget.MaxPort
            ? ExternalOscPortCheck.OutOfRange
            : conflicts ? ExternalOscPortCheck.Conflicting : ExternalOscPortCheck.Valid;
    }
}
