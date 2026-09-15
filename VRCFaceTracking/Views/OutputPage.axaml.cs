using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.DependencyInjection;
using VRCFaceTracking.Services.Logging;
using VRCFaceTracking.ViewModels;

namespace VRCFaceTracking.Views;

public partial class OutputPage : UserControl
{
    private OutputViewModel ViewModel => (OutputViewModel)DataContext!;
    private const double StickThreshold = 40;
    private bool _snapping;
    private bool _autoScroll = true;

    public OutputPage()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<OutputViewModel>();

        LogItems.AddHandler(ScrollViewer.ScrollChangedEvent, OnLogItemsScrollChanged, RoutingStrategies.Bubble);
    }

    private void OnLogItemsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!_autoScroll || _snapping || LogItems.Scroll is not { } scroll)
            return;

        if (e.ExtentDelta.Y <= 0)
            return;

        var prevExtent = scroll.Extent.Height - e.ExtentDelta.Y;
        var prevTarget = prevExtent - scroll.Viewport.Height;
        var prevOffsetY = scroll.Offset.Y - e.OffsetDelta.Y;
        var wasAtBottom = prevTarget <= 0 || prevTarget - prevOffsetY <= StickThreshold;
        if (!wasAtBottom)
            return;

        var target = scroll.Extent.Height - scroll.Viewport.Height;
        if (target <= 0 || scroll.Offset.Y >= target)
            return;

        _snapping = true;
        try { scroll.Offset = scroll.Offset.WithY(target); }
        finally { _snapping = false; }
    }

    private static string JoinedLogText()
        => string.Join(Environment.NewLine, OutputPageLogger.AllLogs.Select(l => l.Message));

    private async void CopyToClipboard_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;

        try
        {
            await clipboard.SetTextAsync(JoinedLogText());
            StatusText.Text = Strings.Resources.CopiedToClipboard;
        }
        catch
        {
            StatusText.Text = Strings.Resources.OutputActionFailed;
        }
    }

    private async void SaveToFile_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        IStorageFile? file;
        try
        {
            file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Strings.Resources.SaveLogPickerTitle,
                SuggestedFileName = $"vrcft-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                FileTypeChoices = [new FilePickerFileType("Text") { Patterns = ["*.txt"] }]
            });
        }
        catch
        {
            StatusText.Text = Strings.Resources.OutputActionFailed;
            return;
        }

        if (file == null)
        {
            StatusText.Text = Strings.Resources.OperationCancelled;
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(JoinedLogText());
            StatusText.Text = string.Format(Strings.Resources.LogSaved, file.Name);
        }
        catch
        {
            StatusText.Text = string.Format(Strings.Resources.LogSaveFailed, file.Name);
        }
    }

    private async void OpenLogsFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Core.Utils.LogDirectory;
            Directory.CreateDirectory(dir);
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher != null)
            {
                await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(dir));
            }
        }
        catch
        {
            StatusText.Text = Strings.Resources.OutputActionFailed;
        }
    }

    private void ClearLogs_Click(object? sender, RoutedEventArgs e)
        => OutputPageLogger.AllLogs.Clear();

    private void AutoScrollToggle_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        _autoScroll = sender is ToggleButton { IsChecked: true };
        if (_autoScroll && LogItems?.Scroll is { } scroll)
        {
            var target = scroll.Extent.Height - scroll.Viewport.Height;
            if (target > 0)
            {
                scroll.Offset = scroll.Offset.WithY(target);
            }
        }
    }
}
