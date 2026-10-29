using System.ComponentModel;

namespace VRCFaceTracking.Core.Library;

public sealed class ModuleMetadataInternal : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<Stream>? StaticImages
    {
        get; set;
    }
    public string Name
    {
        get; set;
    } = string.Empty;
    public string ModulePath
    {
        get; set;
    } = string.Empty;
    public bool IsPlaceholder
    {
        get; set;
    }

    public bool Active
    {
        get;
        set
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Active)));
        }
    }

    public bool UsingEye
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UsingEye)));
        }
    }

    public bool UsingExpression
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UsingExpression)));
        }
    }

    public TrackingCapability EffectiveCapabilities
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveCapabilities)));
        }
    }

    public TrackingCapability? AllowedCapabilities
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllowedCapabilities)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAutomatic)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllowEyes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllowBrows)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllowMouth)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllowTongue)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllowHead)));
        }
    }

    public bool IsAutomatic => AllowedCapabilities == null;

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

    private bool GetAllowed(TrackingCapability flag) => (AllowedCapabilities ?? TrackingCapability.All).Allows(flag);

    private void SetAllowed(TrackingCapability flag, bool value)
    {
        TrackingCapability current = AllowedCapabilities ?? TrackingCapability.All;
        AllowedCapabilities = value ? current | flag : current & ~flag;
    }

    public bool SupportedEye
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SupportedEye)));
        }
    }

    public bool SupportedExpression
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SupportedExpression)));
        }
    }

    public bool Crashed
    {
        get;
        set
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Crashed)));
        }
    }

    public string CrashDescription
    {
        get;
        set
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CrashDescription)));
        }
    } = string.Empty;
}
