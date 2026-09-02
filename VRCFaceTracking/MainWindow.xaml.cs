using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Helpers;

namespace VRCFaceTracking;

public sealed partial class MainWindow : WindowEx
{
    public MainWindow()
    {
        InitializeComponent();

        AppWindow.Closing += async (window, args) =>
        {
            args.Cancel = true;
            await CloseAfterTeardown();
        };

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        Content = null;
        Title = "AppDisplayName".GetLocalized();
    }

    public async Task CloseAfterTeardown()
    {
        await App.GetService<IMainService>().Teardown();
        Close();
    }
}
