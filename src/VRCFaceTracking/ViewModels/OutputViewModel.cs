using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCFaceTracking.Models;
using VRCFaceTracking.Services.Logging;

namespace VRCFaceTracking.ViewModels;

public partial class OutputViewModel : ObservableRecipient
{
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ObservableCollection<LogLine> Logs => OutputPageLogger.AllLogs;
}
