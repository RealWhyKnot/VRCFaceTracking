using CommunityToolkit.Mvvm.ComponentModel;

namespace VRCFaceTracking.Core;

public partial class DeveloperSettings : ObservableObject
{
    public const string SettingKey = "DeveloperMode";

    [ObservableProperty] private bool _enabled = BuildInfo.Channel != BuildChannel.Release;
}
