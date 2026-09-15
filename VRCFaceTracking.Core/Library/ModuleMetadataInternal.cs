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
    public bool IsPlaceholder
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

    private TrackingCapability? _allowedCapabilities;

    public TrackingCapability? AllowedCapabilities
    {
        get => _allowedCapabilities;
        set
        {
            if (_allowedCapabilities == value)
            {
                return;
            }
            _allowedCapabilities = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllowedCapabilities)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAutomatic)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllowEyes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllowBrows)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllowMouth)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllowTongue)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllowHead)));
        }
    }

    public bool IsAutomatic => _allowedCapabilities == null;

    public bool AllowEyes
    {
        get => GetAllowed(TrackingCapability.Eyes);
        set => SetAllowed(TrackingCapability.Eyes, value);
    }

    public bool AllowBrows
    {
        get => GetAllowed(TrackingCapability.Brows);
        set => SetAllowed(TrackingCapability.Brows, value);
    }

    public bool AllowMouth
    {
        get => GetAllowed(TrackingCapability.Mouth);
        set => SetAllowed(TrackingCapability.Mouth, value);
    }

    public bool AllowTongue
    {
        get => GetAllowed(TrackingCapability.Tongue);
        set => SetAllowed(TrackingCapability.Tongue, value);
    }

    public bool AllowHead
    {
        get => GetAllowed(TrackingCapability.Head);
        set => SetAllowed(TrackingCapability.Head, value);
    }

    public void ResetCapabilitiesToAutomatic() => AllowedCapabilities = null;

    private bool GetAllowed(TrackingCapability flag) => (_allowedCapabilities ?? TrackingCapability.All).HasFlag(flag);

    private void SetAllowed(TrackingCapability flag, bool value)
    {
        var current = _allowedCapabilities ?? TrackingCapability.All;
        AllowedCapabilities = value ? current | flag : current & ~flag;
    }

    private bool _supportedEye;
    private bool _supportedExpression;

    public bool SupportedEye
    {
        get => _supportedEye;
        set
        {
            if (_supportedEye == value)
            {
                return;
            }
            _supportedEye = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SupportedEye)));
        }
    }

    public bool SupportedExpression
    {
        get => _supportedExpression;
        set
        {
            if (_supportedExpression == value)
            {
                return;
            }
            _supportedExpression = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SupportedExpression)));
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