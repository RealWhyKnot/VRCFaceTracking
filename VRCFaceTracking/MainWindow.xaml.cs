using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Helpers;

namespace VRCFaceTracking;

public sealed partial class MainWindow : WindowEx
{
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();

        AppWindow.Closing += async (window, args) =>
        {
            if (_isClosing)
            {
                return;
            }
            args.Cancel = true;
            await CloseAfterTeardown();
        };

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        Content = null;
        Title = "AppDisplayName".GetLocalized();
    }

    public async Task CloseAfterTeardown()
    {
        if (_isClosing)
        {
            return;
        }
        _isClosing = true;

        UnifiedLibManager.MarkAppShutdown();
        try
        {
            App.GetService<Services.OpenVRService>().StopService();
        }
        catch (Exception)
        {
        }
        try
        {
            await App.GetService<IMainService>().Teardown();
        }
        catch (Exception ex)
        {
            App.GetService<ILoggerFactory>().CreateLogger<MainWindow>().LogError(ex, "Teardown failed; closing anyway");
        }
        await App.StopHostAsync();
        try
        {
            UnifiedLibManager.ShutdownSandboxServer();
        }
        catch (Exception)
        {
        }
        Close();
    }
}
