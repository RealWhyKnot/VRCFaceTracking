using System.ComponentModel;
using VRCFaceTracking.Core.Library;

namespace VRCFaceTracking;

// Internal version of ModuleMetadata, done to not break compat
public class ModuleMetadataInternal : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    public List<Stream> StaticImages
    {
        get; set;
    }
    public string Name
    {
        get; set;
    }
    public string ModulePath
    {
        get; set;
    }
    private bool _active;

    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Active)));
        }
    }

    //Temporary for the menu display
    private bool _usingEye;
    private bool _usingExpression;


    public bool UsingEye
    {
        get => _usingEye;
        set
        {
            if (_usingEye == value)
            {
                return;
            }
            _usingEye = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UsingEye)));
        }
    }

    public bool UsingExpression
    {
        get => _usingExpression;
        set
        {
            if (_usingExpression == value)
            {
                return;
            }
            _usingExpression = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UsingExpression)));
        }
    }

    private TrackingCapability _effectiveCapabilities;

    public TrackingCapability EffectiveCapabilities
    {
        get => _effectiveCapabilities;
        set
        {
            if (_effectiveCapabilities == value)
            {
                return;
            }
            _effectiveCapabilities = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveCapabilities)));
        }
    }

    private bool _crashed;
    private string _crashDescription = string.Empty;

    public bool Crashed
    {
        get => _crashed;
        set
        {
            _crashed = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Crashed)));
        }
    }

    public string CrashDescription
    {
        get => _crashDescription;
        set
        {
            _crashDescription = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CrashDescription)));
        }
    }
}