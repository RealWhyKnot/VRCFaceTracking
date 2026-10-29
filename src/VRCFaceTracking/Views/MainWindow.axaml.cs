using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Library;

namespace VRCFaceTracking.Views;

public partial class MainWindow : Window
{
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "WindowIcon.ico");
            if (File.Exists(iconPath))
            {
                using var stream = File.OpenRead(iconPath);
                Icon = new WindowIcon(stream);
            }
        }
        catch { }

        Closing += async (_, args) =>
        {
            if (_isClosing)
            {
                return;
            }
            args.Cancel = true;
            await CloseAfterTeardown();
        };
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
        try
        {
            await App.GetService<ILocalSettingsService>().FlushAsync();
        }
        catch (Exception ex)
        {
            App.GetService<ILoggerFactory>().CreateLogger<MainWindow>().LogError(ex, "Flushing settings failed; closing anyway");
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
