using CommunityToolkit.Mvvm.ComponentModel;

namespace VRCFaceTracking.ViewModels;

internal sealed partial class OutputViewModel : ObservableRecipient
{
    [ObservableProperty] private string _statusMessage = string.Empty;
}
