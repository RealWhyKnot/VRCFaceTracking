using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Helpers;
using VRCFaceTracking.Core.Updates;
using VRCFaceTracking.Strings;

namespace VRCFaceTracking.Services;

internal sealed partial class UpdateService
{
    private static readonly Uri ReleasesUri = new("https://api.github.com/repos/RealWhyKnot/VRCFaceTracking/releases?per_page=20");
    private static readonly string StagingDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "VRCFaceTracking", "update");
    private static readonly string InstallDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
    private static readonly bool Installed = UpdateAssets.IsInstalled(InstallDir);
    private static readonly CompositeFormat UpToDateFormat = CompositeFormat.Parse(Resources.UpdateUpToDateContent);
    private static readonly CompositeFormat DialogFormat = CompositeFormat.Parse(Resources.UpdateDialogContent);

    private readonly ILogger<UpdateService> _logger;
    private readonly UpdateSettings _settings;
    private readonly ILocalSettingsService _localSettings;
    private int _busy;

    public UpdateService(ILogger<UpdateService> logger, UpdateSettings settings, ILocalSettingsService localSettings)
    {
        _logger = logger;
        _settings = settings;
        _localSettings = localSettings;
    }

    public Task CheckOnStartupAsync(CancellationToken cancellationToken)
    {
        if (BuildInfo.Channel == BuildChannel.Dev)
        {
            LogUpdateCheckSkippedDevChannel(_logger);
            return Task.CompletedTask;
        }

        if (!_settings.CheckOnStartup)
        {
            LogUpdateCheckDisabledSettings(_logger);
            return Task.CompletedTask;
        }

        return CheckAsync(manual: false, cancellationToken);
    }

    public async Task CheckAsync(bool manual, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            await CheckCoreAsync(manual, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or IOException or UnauthorizedAccessException || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUpdateCheckFailed(_logger, e);
            if (manual)
            {
                await ShowMessageAsync(Resources.UpdateFailedTitle, Resources.UpdateFailedContent, cancellationToken);
            }
        }
        finally
        {
            _busy = 0;
        }
    }

    private async Task CheckCoreAsync(bool manual, CancellationToken cancellationToken)
    {
        if (!AppVersion.TryParse(BuildInfo.VersionString, out AppVersion current))
        {
            LogUpdateCheckSkippedVersionNot(_logger, BuildInfo.VersionString);
            return;
        }

        LogCheckingReleasesChannelCurrent(_logger, BuildInfo.ChannelName, current);
        GithubRelease? release = await FetchAsync(current, cancellationToken);
        if (release == null)
        {
            LogNoReleaseNewerThanChannel(_logger, current, BuildInfo.ChannelName);
            if (manual)
            {
                await ShowMessageAsync(Resources.UpdateUpToDateTitle, string.Format(CultureInfo.CurrentCulture, UpToDateFormat, BuildInfo.ChannelName), cancellationToken);
            }

            return;
        }

        string assetName = Installed ? UpdateAssets.SetupName(release.TagName) : UpdateAssets.ArchiveName(release.TagName);
        if (!release.Assets.Any(a => string.Equals(a.Name, assetName, StringComparison.Ordinal)))
        {
            LogReleaseNoAssetTreatingNo(_logger, release.TagName, assetName);
            if (manual)
            {
                await ShowMessageAsync(Resources.UpdateUpToDateTitle, string.Format(CultureInfo.CurrentCulture, UpToDateFormat, BuildInfo.ChannelName), cancellationToken);
            }

            return;
        }

        if (!manual && string.Equals(release.TagName, _settings.SkippedTag, StringComparison.Ordinal))
        {
            LogReleaseAvailableButSkippedUser(_logger, release.TagName);
            return;
        }

        LogReleaseNewerThan(_logger, release.TagName, current);
        await OfferAsync(release, cancellationToken);
    }

    private async Task OfferAsync(GithubRelease release, CancellationToken cancellationToken)
    {
        ContentDialogResult choice = await ShowDialogAsync(() => new ContentDialog
        {
            Title = Resources.UpdateDialogTitle,
            Content = string.Format(CultureInfo.CurrentCulture, DialogFormat, release.TagName),
            PrimaryButtonText = Resources.UpdateDialogUpdate,
            SecondaryButtonText = Resources.UpdateDialogSkip,
            CloseButtonText = Resources.UpdateDialogLater,
            DefaultButton = ContentDialogButton.Primary,
        }, cancellationToken);

        switch (choice)
        {
            case ContentDialogResult.Primary:
                await InstallAsync(release, cancellationToken);
                break;
            case ContentDialogResult.Secondary:
                _settings.SkippedTag = release.TagName;
                await _localSettings.SaveAsync(_settings, cancellationToken);
                LogUserSkippedRelease(_logger, release.TagName);
                break;
            case ContentDialogResult.None:
            default:
                LogUserDeferredRelease(_logger, release.TagName);
                break;
        }
    }

    private async Task<GithubRelease?> FetchAsync(AppVersion current, CancellationToken cancellationToken)
    {
        using HttpClient client = CreateClient();
        string json = await client.GetStringAsync(ReleasesUri, cancellationToken);
        List<GithubRelease> releases = JsonSerializer.Deserialize<List<GithubRelease>>(json) ?? [];
        LogFetchedReleases(_logger, releases.Count);
        return ReleaseSelector.Select(releases, current, BuildInfo.Channel);
    }

    private async Task InstallAsync(GithubRelease release, CancellationToken cancellationToken)
    {
        string archiveName = Installed ? UpdateAssets.SetupName(release.TagName) : UpdateAssets.ArchiveName(release.TagName);
        string integrityName = Installed ? UpdateAssets.SetupIntegrityName(release.TagName) : UpdateAssets.IntegrityName(release.TagName);
        GithubReleaseAsset archiveAsset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, archiveName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Release {release.TagName} has no asset {archiveName}");
        GithubReleaseAsset integrityAsset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, integrityName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Release {release.TagName} has no asset {integrityName}");

        ProgressBar? bar = null;
        ContentDialog? progress = null;
        await OnUiAsync(() =>
        {
            bar = new ProgressBar { Minimum = 0, Maximum = 1, IsIndeterminate = true };
            progress = new ContentDialog
            {
                Title = Resources.UpdateDownloadingTitle,
                Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = archiveName }, bar } },
            };
            _ = progress.ShowAsync();
        }, cancellationToken);

        try
        {
            string archivePath = await DownloadVerifiedAsync(archiveAsset, integrityAsset, archiveName, fraction => PostToUi(() =>
            {
                bar!.IsIndeterminate = false;
                bar.Value = fraction;
            }), cancellationToken);

            PostToUi(() => bar!.IsIndeterminate = true);
            await StartHelperAsync(release.TagName, archiveName, archivePath, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException or IOException or UnauthorizedAccessException or Win32Exception || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUpdateFailed(_logger, e, release.TagName);
            PostToUi(() => progress?.Hide());
            await ShowMessageAsync(Resources.UpdateFailedTitle, Resources.UpdateFailedContent, cancellationToken);
            return;
        }

        PostToUi(() => progress?.Hide());
    }

    private async Task<string> DownloadVerifiedAsync(GithubReleaseAsset archiveAsset, GithubReleaseAsset integrityAsset, string archiveName, Action<double> progress, CancellationToken cancellationToken)
    {
        if (Directory.Exists(StagingDir))
        {
            Directory.Delete(StagingDir, recursive: true);
        }

        Directory.CreateDirectory(StagingDir);
        using HttpClient client = CreateClient();
        Uri integrityUrl = integrityAsset.BrowserDownloadUrl ?? throw new InvalidDataException($"{integrityAsset.Name} has no download URL");
        Uri archiveUrl = archiveAsset.BrowserDownloadUrl ?? throw new InvalidDataException($"{archiveAsset.Name} has no download URL");
        string integrity = await client.GetStringAsync(integrityUrl, cancellationToken);
        (string? sha256, long size) = UpdateAssets.ParseArchiveEntry(integrity, archiveName);
        string archivePath = Path.Combine(StagingDir, archiveName);
        LogDownloadingBytes(_logger, archiveName, size);
        await DownloadAsync(client, archiveUrl, archivePath, size, progress, cancellationToken);

        long actualSize = new FileInfo(archivePath).Length;
        if (actualSize != size)
        {
            throw new InvalidDataException($"{archiveName} is {actualSize} bytes, expected {size}");
        }

        string actualHash;
        await using (FileStream stream = File.OpenRead(archivePath))
        {
            actualHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        }

        return string.Equals(actualHash, sha256, StringComparison.OrdinalIgnoreCase)
            ? archivePath
            : throw new InvalidDataException($"{archiveName} sha256 {actualHash} does not match {sha256}");
    }

    private async Task StartHelperAsync(string tag, string archiveName, string archivePath, CancellationToken cancellationToken)
    {
        string logPath = Path.Combine(Core.Logging.LogDirectories.Current, "update.log");
        string exePath = Path.Combine(InstallDir, UpdateAssets.ExeName);
        if (Installed)
        {
            string setupScript = Path.Combine(StagingDir, "apply.ps1");
            UpdateHelperScript.WritePowerShell(setupScript, UpdateHelperScript.BuildSetup(Environment.ProcessId, archivePath, StagingDir, InstallDir, exePath, logPath));
            Process.Start(PowerShell(setupScript));
            LogSetupHelperStarted(_logger, tag, archiveName);
            PostToUi(() => _ = App.MainWindow!.CloseAfterTeardownAsync(CancellationToken.None));
            return;
        }

        string extracted = Path.Combine(StagingDir, "extracted");
        await Task.Run(() => ExtractAsync(archivePath, extracted, cancellationToken), cancellationToken);
        string payloadRoot = UpdateAssets.ResolvePayloadRoot(extracted);

        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            string scriptPath = Path.Combine(StagingDir, "apply.ps1");
            UpdateHelperScript.WritePowerShell(scriptPath, UpdateHelperScript.Build(Environment.ProcessId, payloadRoot, StagingDir, InstallDir, exePath, logPath));
            psi = PowerShell(scriptPath);
            psi.WorkingDirectory = InstallDir;
        }
        else
        {
            string scriptPath = Path.Combine(StagingDir, "apply.sh");
            await File.WriteAllTextAsync(scriptPath, UpdateHelperScript.BuildSh(Environment.ProcessId, payloadRoot, StagingDir, InstallDir, exePath, logPath), cancellationToken);
            psi = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                WorkingDirectory = InstallDir,
            };
            psi.ArgumentList.Add(scriptPath);
        }

        Process.Start(psi);
        LogUpdateHelperStarted(_logger, tag);
        PostToUi(() => _ = App.MainWindow!.CloseAfterTeardownAsync(CancellationToken.None));
    }

    private static async Task ExtractAsync(string archivePath, string destination, CancellationToken cancellationToken)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await ZipFile.ExtractToDirectoryAsync(archivePath, destination, overwriteFiles: true, cancellationToken);
            return;
        }

        Directory.CreateDirectory(destination);
        await using FileStream file = File.OpenRead(archivePath);
        await using var gzStream = new GZipStream(file, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzStream, destination, overwriteFiles: true, cancellationToken);
    }

    private static ProcessStartInfo PowerShell(string scriptPath)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (string? arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", scriptPath })
        {
            psi.ArgumentList.Add(arg);
        }

        return psi;
    }

    private static async Task DownloadAsync(HttpClient client, Uri url, string path, long expectedSize, Action<double> progress, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream target = File.Create(path);
        byte[] buffer = new byte[81920];
        long total = 0;
        int lastPercent = -1;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
            int percent = (int)(total * 100 / expectedSize);
            if (percent != lastPercent)
            {
                lastPercent = percent;
                progress(Math.Min(1.0, total / (double)expectedSize));
            }
        }
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = HappyEyeballsHttp.CreateHttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VRCFaceTracking", BuildInfo.VersionString));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static Task<ContentDialogResult> ShowMessageAsync(string title, string content, CancellationToken cancellationToken) => ShowDialogAsync(() => new ContentDialog
    {
        Title = title,
        Content = content,
        CloseButtonText = Resources.UpdateUpToDateClose,
    }, cancellationToken);

    private static async Task<ContentDialogResult> ShowDialogAsync(Func<ContentDialog> build, CancellationToken cancellationToken)
    {
        Task<ContentDialogResult> shown = await Dispatcher.UIThread.InvokeAsync(() => build().ShowAsync(), DispatcherPriority.Normal, cancellationToken);
        return await shown;
    }

    private static async Task OnUiAsync(Action action, CancellationToken cancellationToken) =>
        await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken);

    private static void PostToUi(Action action) => Dispatcher.UIThread.Post(action);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Update check skipped on dev channel")]
    private static partial void LogUpdateCheckSkippedDevChannel(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Update check disabled in settings")]
    private static partial void LogUpdateCheckDisabledSettings(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Update check skipped: version '{Version}' is not in YYYY.M.D.N form")]
    private static partial void LogUpdateCheckSkippedVersionNot(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Checking releases for the {Channel} channel, current {Version}")]
    private static partial void LogCheckingReleasesChannelCurrent(ILogger logger, string channel, AppVersion version);

    [LoggerMessage(Level = LogLevel.Information, Message = "No release newer than {Version} on the {Channel} channel")]
    private static partial void LogNoReleaseNewerThanChannel(ILogger logger, AppVersion version, string channel);

    [LoggerMessage(Level = LogLevel.Information, Message = "Release {Tag} has no asset {Archive}; treating as no update")]
    private static partial void LogReleaseNoAssetTreatingNo(ILogger logger, string tag, string archive);

    [LoggerMessage(Level = LogLevel.Information, Message = "Release {Tag} available but skipped by user")]
    private static partial void LogReleaseAvailableButSkippedUser(ILogger logger, string tag);

    [LoggerMessage(Level = LogLevel.Information, Message = "Release {Tag} is newer than {Version}")]
    private static partial void LogReleaseNewerThan(ILogger logger, string tag, AppVersion version);

    [LoggerMessage(Level = LogLevel.Information, Message = "User skipped release {Tag}")]
    private static partial void LogUserSkippedRelease(ILogger logger, string tag);

    [LoggerMessage(Level = LogLevel.Information, Message = "User deferred release {Tag}")]
    private static partial void LogUserDeferredRelease(ILogger logger, string tag);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Update check failed")]
    private static partial void LogUpdateCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetched {Count} releases")]
    private static partial void LogFetchedReleases(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloading {Archive} ({Size} bytes)")]
    private static partial void LogDownloadingBytes(ILogger logger, string archive, long size);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update helper started for {Tag} with {Setup}; closing to apply")]
    private static partial void LogSetupHelperStarted(ILogger logger, string tag, string setup);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update helper started for {Tag}; closing to apply")]
    private static partial void LogUpdateHelperStarted(ILogger logger, string tag);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Update to {Tag} failed")]
    private static partial void LogUpdateFailed(ILogger logger, Exception exception, string tag);
}
