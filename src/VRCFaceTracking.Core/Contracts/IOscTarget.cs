using System.ComponentModel;

namespace VRCFaceTracking.Core.Contracts;

public interface IOscTarget : INotifyPropertyChanged
{
    bool IsConnected
    {
        get;
        set;
    }

    int InPort
    {
        get;
        set;
    }

    int OutPort
    {
        get;
        set;
    }

    string? DestinationAddress
    {
        get;
        set;
    }
}
