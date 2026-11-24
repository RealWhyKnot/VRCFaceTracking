using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Validation;

namespace VRCFaceTracking.Core.Models;

public sealed partial class OscTarget : ObservableValidator, IOscTarget
{
    public const int MinPort = 1;
    public const int MaxPort = 65535;

    [ObservableProperty] private bool _isConnected;

    [ObservableProperty]
    [Range(MinPort, MaxPort)]
    [property: SavedSetting("OSCInPort", 9001)]
    private int _inPort;

    [ObservableProperty]
    [Range(MinPort, MaxPort)]
    [property: SavedSetting("OSCOutPort", 9000)]
    private int _outPort;

    [ObservableProperty]
    [ValidIpAddress]
    [property: SavedSetting("OSCAddress", "127.0.0.1")]
    private string? _destinationAddress;

    public OscTarget(ILocalSettingsService localSettingsService)
    {
        PropertyChanged += (_, _) => _ = localSettingsService.SaveAsync(this, CancellationToken.None);
    }
}
