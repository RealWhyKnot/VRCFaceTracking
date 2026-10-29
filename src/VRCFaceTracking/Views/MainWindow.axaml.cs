using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Library;

namespace VRCFaceTracking.Views;

internal sealed partial class MainWindow : Window
{
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "WindowIcon.ico");
        try
        {
            if (File.Exists(iconPath))
            {
                using FileStream stream = File.OpenRead(iconPath);
                Icon = new WindowIcon(stream);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogWindowIconNotLoaded(App.GetService<ILoggerFactory>().CreateLogger<MainWindow>(), iconPath, e.Message);
        }

        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_isClosing)
        {
            return;
        }
        args.Cancel = true;
        await CloseAfterTeardownAsync(CancellationToken.None);
    }

    public async Task CloseAfterTeardownAsync(CancellationToken cancellationToken)
    {
        if (_isClosing)
        {
            return;
        }
        _isClosing = true;

        try
        {
            await TeardownAsync(cancellationToken);
        }
        finally
        {
            Close();
        }
    }

    private static async Task TeardownAsync(CancellationToken cancellationToken)
    {
        UnifiedLibManager.MarkAppShutdown();
        App.GetService<Services.OpenVRService>().StopService();
        try
        {
            await App.GetService<IMainService>().TeardownAsync(cancellationToken);
        }
        finally
        {
            await App.GetService<ILocalSettingsService>().FlushAsync(cancellationToken);
        }
        await App.StopHostAsync(cancellationToken);
        UnifiedLibManager.ShutdownSandboxServer();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window icon {Path} could not be loaded: {Message}")]
    private static partial void LogWindowIconNotLoaded(ILogger logger, string path, string message);
}
