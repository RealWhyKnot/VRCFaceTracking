using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.ViewModels;

internal sealed partial class MainViewModel : ObservableRecipient
{
    public ILibManager LibManager
    {
        get;
    }
    public OscQueryService ParameterOutputService
    {
        get;
    }
    public OscRecvService OscRecvService
    {
        get;
    }
    public OscSendService OscSendService
    {
        get;
    }
    public IOscTarget OscTarget
    {
        get;
    }
    private readonly IModuleDataService _moduleDataService;

    private int _messagesRecvd;
    [ObservableProperty] private int _messagesInPerSec;

    private int _messagesSent;
    [ObservableProperty] private int _messagesOutPerSec;

    [ObservableProperty] private bool _noModulesInstalled;

    [ObservableProperty] private bool _oscWasDisabled;

    private readonly DispatcherTimer _msgCounterTimer;

    public MainViewModel(
        ILibManager libManager,
        OscQueryService parameterOutputService,
        IModuleDataService moduleDataService,
        IOscTarget oscTarget,
        OscRecvService oscRecvService,
        OscSendService oscSendService
        )
    {
        LibManager = libManager;
        ParameterOutputService = parameterOutputService;
        OscTarget = oscTarget;
        OscRecvService = oscRecvService;
        OscSendService = oscSendService;
        _moduleDataService = moduleDataService;

        OscRecvService.OnMessagesReceived += MessageReceived;
        OscSendService.OnMessagesDispatched += MessageDispatched;
        _msgCounterTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _msgCounterTimer.Tick += (_, _) =>
        {
            MessagesInPerSec = Interlocked.Exchange(ref _messagesRecvd, 0);
            MessagesOutPerSec = Interlocked.Exchange(ref _messagesSent, 0);
        };
        _msgCounterTimer.Start();

        OnNavigatedTo();
    }

    public void OnNavigatedTo()
    {
        IEnumerable<InstallableTrackingModule> installedNewModules = _moduleDataService.GetInstalledModules();
        int installedLegacyModules = _moduleDataService.GetLegacyModules().Count();
        NoModulesInstalled = !installedNewModules.Any() && installedLegacyModules == 0;
    }

    private void MessageReceived(int msgCount) => Interlocked.Add(ref _messagesRecvd, msgCount);
    private void MessageDispatched(int msgCount) => Interlocked.Add(ref _messagesSent, msgCount);
}
