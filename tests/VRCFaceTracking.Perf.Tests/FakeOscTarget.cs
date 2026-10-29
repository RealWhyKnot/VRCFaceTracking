using System.ComponentModel;
using VRCFaceTracking.Core.Contracts;

namespace VRCFaceTracking.Perf.Tests;

internal sealed class FakeOscTarget : IOscTarget
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsConnected
    {
        get; set;
    }
    public int InPort
    {
        get; set;
    }
    public string? DestinationAddress { get; set; } = "127.0.0.1";

    public int OutPort
    {
        get;
        set
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OutPort)));
        }
    }
}
