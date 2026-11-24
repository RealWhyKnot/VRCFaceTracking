using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Services;
using VRCFaceTracking.Strings;

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
    private const int LiveIntervalMs = 50;
    private const int StatusEvery = 10;

    private readonly IModuleDataService _moduleDataService;
    private readonly UnifiedTrackingMutator _mutator;
    private readonly DispatcherTimer _liveTimer;
    private int _liveTick;

    private static readonly System.Text.CompositeFormat ConnectedFormat = System.Text.CompositeFormat.Parse(Resources.HomeOsc_Connected);

    private int _messagesRecvd;
    [ObservableProperty] private int _messagesInPerSec;

    private int _messagesSent;
    [ObservableProperty] private int _messagesOutPerSec;

    [ObservableProperty] private bool _noModulesInstalled;

    [ObservableProperty] private bool _oscWasDisabled;

    [ObservableProperty] private string _connectionText = Resources.HomeOsc_NotConnected;

    private readonly DispatcherTimer _msgCounterTimer;

    public MainViewModel(
        ILibManager libManager,
        OscQueryService parameterOutputService,
        IModuleDataService moduleDataService,
        IOscTarget oscTarget,
        OscRecvService oscRecvService,
        OscSendService oscSendService,
        UnifiedTrackingMutator mutator
        )
    {
        LibManager = libManager;
        ParameterOutputService = parameterOutputService;
        OscTarget = oscTarget;
        OscRecvService = oscRecvService;
        OscSendService = oscSendService;
        _moduleDataService = moduleDataService;
        _mutator = mutator;
        Tracking = new HomeTrackingViewModel();
        Cameras = new CameraStreamsViewModel();
        _liveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(LiveIntervalMs) };
        _liveTimer.Tick += (_, _) => OnLiveTick();
        OscTarget.PropertyChanged += (_, _) => RefreshConnection();
        RefreshConnection();

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

    public HomeTrackingViewModel Tracking
    {
        get;
    }

    public CameraStreamsViewModel Cameras
    {
        get;
    }

    public bool IsLive => _liveTimer.IsEnabled;

    public void Start()
    {
        if (_liveTimer.IsEnabled)
        {
            return;
        }

        LibManager.SetImageStreamEnabled(enabled: true);
        _liveTick = 0;
        RefreshStatus();
        RefreshCameras();
        _liveTimer.Start();
    }

    public void Stop()
    {
        if (!_liveTimer.IsEnabled)
        {
            return;
        }

        _liveTimer.Stop();
        LibManager.SetImageStreamEnabled(enabled: false);
        Cameras.Reset();
    }

    public void RefreshStatus() => Tracking.Refresh(LibManager.GetOwnershipSnapshot(), _mutator.Activity, ParameterOutputService.GapReport);

    public void RefreshCameras() => Cameras.Refresh(UnifiedTracking.EyeImageData, UnifiedTracking.LipImageData);

    private void OnLiveTick()
    {
        if (++_liveTick >= StatusEvery)
        {
            _liveTick = 0;
            RefreshStatus();
        }

        RefreshCameras();
    }

    private void RefreshConnection()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshConnection);
            return;
        }

        ConnectionText = OscTarget.IsConnected
            ? string.Format(CultureInfo.CurrentCulture, ConnectedFormat, OscTarget.InPort, OscTarget.DestinationAddress, OscTarget.OutPort)
            : Resources.HomeOsc_NotConnected;
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
