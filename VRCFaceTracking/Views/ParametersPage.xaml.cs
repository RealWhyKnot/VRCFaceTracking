using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.ViewModels;

namespace VRCFaceTracking.Views;

public sealed partial class ParametersPage : Page
{
    public ParametersViewModel ViewModel
    {
        get;
    }

    public IMainService MainService
    {
        get;
    }

    private readonly ObservableCollection<ParameterDebugUserControl> _trackedParameters = new();
    private readonly Dictionary<string, ParameterDebugUserControl> _controlsByName = new();

    public ParametersPage()
    {
        ViewModel = App.GetService<ParametersViewModel>();
        MainService = App.GetService<IMainService>();
        var dispatcher = App.GetService<IDispatcherService>();

        this.DataContext = this;
        InitializeComponent();

        MainService.ParameterUpdate += (addr, val) => dispatcher.Run(() => OnParameterSend(addr, val));
    }

    private void OnParameterSend(string address, float value)
    {
        var slash = address.LastIndexOf('/');
        var name = slash >= 0 ? address[(slash + 1)..] : address;

        if (_controlsByName.TryGetValue(name, out var existingControl))
        {
            existingControl.ViewModel.ParameterValue = value;
            return;
        }

        var newControl = new ParameterDebugUserControl();
        newControl.ViewModel.ParameterName = name;
        newControl.ViewModel.ParameterValue = value;
        _controlsByName[name] = newControl;
        _trackedParameters.Add(newControl);
    }
}
