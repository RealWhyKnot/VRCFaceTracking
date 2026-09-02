using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Services;
using VRCFaceTracking.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;

namespace VRCFaceTracking.Views;

public sealed partial class OutputPage : Page
{
    public OutputViewModel ViewModel
    {
        get;
    }

    public ObservableCollection<LogLine> FilteredLog => OutputPageLogger.FilteredLogs;
    public ObservableCollection<LogLine> AllLog => OutputPageLogger.AllLogs;

    public OutputPage()
    {
        ViewModel = App.GetService<OutputViewModel>();
        InitializeComponent();
    }

    private void ScrollToBottom() => LogScroller.ChangeView(null, LogScroller.ScrollableHeight, null);

    private async void SaveToFile_OnClick(object sender, RoutedEventArgs e)
    {
        // Create a file picker
        FileSavePicker savePicker = new Windows.Storage.Pickers.FileSavePicker();

        // Retrieve the window handle (HWND) of the current WinUI 3 window.
        var window = App.MainWindow;
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

        // Initialize the file picker with the window handle (HWND).
        WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);

        // Set options for your file picker
        savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        savePicker.FileTypeChoices.Add("Plain Text", new List<string>() { ".txt" });
        DateTime now = DateTime.Now;
        string format = CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern + "-" + CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern;
        var dateFormatted = now.ToString(format);
        savePicker.SuggestedFileName = $"VRCFaceTracking.txt-{dateFormatted}";

        // Open the picker for the user to pick a file
        StorageFile file = await savePicker.PickSaveFileAsync();
        if (file != null)
        {
            CachedFileManager.DeferUpdates(file);

            // write to file
            var logString = string.Join("\n", AllLog.Select(log => log.Message));
            await FileIO.AppendTextAsync(file, logString);

            FileUpdateStatus status = await CachedFileManager.CompleteUpdatesAsync(file);
            if (status == FileUpdateStatus.Complete)
            {
                SaveStatus.Text = string.Format("LogSaved".GetLocalized(), file.Name);
            }
            else if (status == FileUpdateStatus.CompleteAndRenamed)
            {
                SaveStatus.Text = string.Format("LogSavedRenamed".GetLocalized(), file.Name);
            }
            else
            {
                SaveStatus.Text = string.Format("LogSaveFailed".GetLocalized(), file.Name);
            }
        }
        else
        {
            SaveStatus.Text = "OperationCancelled".GetLocalized();
        }
    }

    private void CopyToClipboard_OnClick(object sender, RoutedEventArgs e)
    {
        var logString = string.Join("\n", AllLog.Select(log => log.Message));
        var package = new DataPackage();
        package.SetText(logString);
        Clipboard.SetContent(package);
        SaveStatus.Text = "CopiedToClipboard".GetLocalized();
    }

    private void OpenLogsFolder_OnClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Core.Utils.LogDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", Core.Utils.LogDirectory) { UseShellExecute = true });
    }

    private void LogScroller_OnLoaded(object sender, RoutedEventArgs e)
    {
        ScrollToBottom();

        // We need to subscribe to the observablecollection onchanged event to scroll to the bottom. Note that we need a small delay because windows.
        // If we don't then we'll be scrolling a line too short.
        var scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        scrollTimer.Tick += (_, _) =>
        {
            scrollTimer.Stop();
            ScrollToBottom();
        };
        FilteredLog.CollectionChanged += (_, _) =>
        {
            if (!scrollTimer.IsEnabled)
            {
                scrollTimer.Start();
            }
        };
    }
}
