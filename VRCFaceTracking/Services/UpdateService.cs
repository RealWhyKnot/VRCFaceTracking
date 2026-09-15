using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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

public class UpdateService
{
    private const string ReleasesUrl = "https://api.github.com/repos/RealWhyKnot/VRCFaceTracking/releases?per_page=20";
    private static readonly string StagingDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "VRCFaceTracking", "update");

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

    public Task CheckOnStartupAsync()
    {
        if (BuildInfo.Channel == BuildChannel.Dev)
        {
            _logger.LogDebug("Update check skipped on dev channel");
            return Task.CompletedTask;
        }

        if (!_settings.CheckOnStartup)
        {
            _logger.LogDebug("Update check disabled in settings");
            return Task.CompletedTask;
        }

        return CheckAsync(false);
    }

    public async Task CheckAsync(bool manual)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            if (!AppVersion.TryParse(BuildInfo.VersionString, out var current))
            {
                _logger.LogWarning("Update check skipped: version '{version}' is not in YYYY.M.D.N form", BuildInfo.VersionString);
                return;
            }

            _logger.LogDebug("Checking releases for the {channel} channel, current {version}", BuildInfo.ChannelName, current);
            var release = await FetchAsync(current);
            if (release == null)
            {
                _logger.LogInformation("No release newer than {version} on the {channel} channel", current, BuildInfo.ChannelName);
                if (manual)
                {
                    await ShowMessageAsync(Resources.UpdateUpToDateTitle, string.Format(Resources.UpdateUpToDateContent, BuildInfo.ChannelName));
                }

                return;
            }

            if (!release.Assets.Any(a => a.Name == UpdateAssets.ArchiveName(release.TagName)))
            {
                _logger.LogInformation("Release {tag} has no asset {archive}; treating as no update", release.TagName, UpdateAssets.ArchiveName(release.TagName));
                if (manual)
                {
                    await ShowMessageAsync(Resources.UpdateUpToDateTitle, string.Format(Resources.UpdateUpToDateContent, BuildInfo.ChannelName));
                }

                return;
            }

            if (!manual && string.Equals(release.TagName, _settings.SkippedTag, StringComparison.Ordinal))
            {
                _logger.LogInformation("Release {tag} available but skipped by user", release.TagName);
                return;
            }

            _logger.LogInformation("Release {tag} is newer than {version}", release.TagName, current);
            var choice = await ShowDialogAsync(() => new ContentDialog
            {
                Title = Resources.UpdateDialogTitle,
                Content = string.Format(Resources.UpdateDialogContent, release.TagName),
                PrimaryButtonText = Resources.UpdateDialogUpdate,
                SecondaryButtonText = Resources.UpdateDialogSkip,
                CloseButtonText = Resources.UpdateDialogLater,
                DefaultButton = ContentDialogButton.Primary,
            });

            switch (choice)
            {
                case ContentDialogResult.Primary:
                    await InstallAsync(release);
                    break;
                case ContentDialogResult.Secondary:
                    _settings.SkippedTag = release.TagName;
                    await _localSettings.Save(_settings);
                    _logger.LogInformation("User skipped release {tag}", release.TagName);
                    break;
                default:
                    _logger.LogInformation("User deferred release {tag}", release.TagName);
                    break;
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Update check failed");
            if (manual)
            {
                await ShowMessageAsync(Resources.UpdateFailedTitle, Resources.UpdateFailedContent);
            }
        }
        finally
        {
            _busy = 0;
        }
    }

    private async Task<GithubRelease?> FetchAsync(AppVersion current)
    {
        using var client = CreateClient();
        var json = await client.GetStringAsync(ReleasesUrl);
        var releases = JsonSerializer.Deserialize<List<GithubRelease>>(json) ?? new List<GithubRelease>();
        _logger.LogDebug("Fetched {count} releases", releases.Count);
        return ReleaseSelector.Select(releases, current, BuildInfo.Channel);
    }

    private async Task InstallAsync(GithubRelease release)
    {
        var archiveName = UpdateAssets.ArchiveName(release.TagName);
        var integrityName = UpdateAssets.IntegrityName(release.TagName);
        var archiveAsset = release.Assets.FirstOrDefault(a => a.Name == archiveName)
            ?? throw new InvalidDataException($"Release {release.TagName} has no asset {archiveName}");
        var integrityAsset = release.Assets.FirstOrDefault(a => a.Name == integrityName)
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
        });

        try
        {
            if (Directory.Exists(StagingDir))
            {
                Directory.Delete(StagingDir, true);
            }

            Directory.CreateDirectory(StagingDir);
            using var client = CreateClient();
            var (sha256, size) = UpdateAssets.ParseArchiveEntry(await client.GetStringAsync(integrityAsset.BrowserDownloadUrl), archiveName);
            var archivePath = Path.Combine(StagingDir, archiveName);
            _logger.LogInformation("Downloading {archive} ({size} bytes)", archiveName, size);
            await DownloadAsync(client, archiveAsset.BrowserDownloadUrl, archivePath, size, fraction => OnUi(() =>
            {
                bar!.IsIndeterminate = false;
                bar.Value = fraction;
            }));

            var actualSize = new FileInfo(archivePath).Length;
            if (actualSize != size)
            {
                throw new InvalidDataException($"{archiveName} is {actualSize} bytes, expected {size}");
            }

            string actualHash;
            await using (var stream = File.OpenRead(archivePath))
            {
                actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            }

            if (actualHash != sha256)
            {
                throw new InvalidDataException($"{archiveName} sha256 {actualHash} does not match {sha256}");
            }

            OnUi(() => bar!.IsIndeterminate = true);
            var extracted = Path.Combine(StagingDir, "extracted");
            await Task.Run(() =>
            {
                if (archiveName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ZipFile.ExtractToDirectory(archivePath, extracted, true);
                }
                else
                {
                    Directory.CreateDirectory(extracted);
                    using var file = File.OpenRead(archivePath);
                    using var gzStream = new GZipStream(file, CompressionMode.Decompress);
                    TarFile.ExtractToDirectory(gzStream, extracted, overwriteFiles: true);
                }
            });
            var payloadRoot = UpdateAssets.ResolvePayloadRoot(extracted);
            var installDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            var logPath = Path.Combine(Core.Utils.LogDirectory, "update.log");
            var exePath = Path.Combine(installDir, UpdateAssets.ExeName);

            ProcessStartInfo psi;
            if (OperatingSystem.IsWindows())
            {
                var scriptPath = Path.Combine(StagingDir, "apply.ps1");
                File.WriteAllText(scriptPath, UpdateHelperScript.Build(Environment.ProcessId, payloadRoot, StagingDir, installDir, exePath, logPath));
                psi = new ProcessStartInfo("powershell.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = installDir,
                };
                foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", scriptPath })
                {
                    psi.ArgumentList.Add(arg);
                }
            }
            else
            {
                var scriptPath = Path.Combine(StagingDir, "apply.sh");
                File.WriteAllText(scriptPath, UpdateHelperScript.BuildSh(Environment.ProcessId, payloadRoot, StagingDir, installDir, exePath, logPath));
                psi = new ProcessStartInfo("/bin/sh")
                {
                    UseShellExecute = false,
                    WorkingDirectory = installDir,
                };
                psi.ArgumentList.Add(scriptPath);
            }

            Process.Start(psi);
            _logger.LogInformation("Update helper started for {tag}; closing to apply", release.TagName);
            OnUi(() => _ = App.MainWindow!.CloseAfterTeardown());
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Update to {tag} failed", release.TagName);
            OnUi(() => progress?.Hide());
            await ShowMessageAsync(Resources.UpdateFailedTitle, Resources.UpdateFailedContent);
            return;
        }

        OnUi(() => progress?.Hide());
    }

    private static async Task DownloadAsync(HttpClient client, string url, string path, long expectedSize, Action<double> progress)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var target = File.Create(path);
        var buffer = new byte[81920];
        long total = 0;
        var lastPercent = -1;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read));
            total += read;
            var percent = (int)(total * 100 / expectedSize);
            if (percent != lastPercent)
            {
                lastPercent = percent;
                progress(Math.Min(1.0, total / (double)expectedSize));
            }
        }
    }

    private static HttpClient CreateClient()
    {
        var client = HappyEyeballsHttp.CreateHttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VRCFaceTracking", BuildInfo.VersionString));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static Task ShowMessageAsync(string title, string content) => ShowDialogAsync(() => new ContentDialog
    {
        Title = title,
        Content = content,
        CloseButtonText = Resources.UpdateUpToDateClose,
    });

    private static Task<ContentDialogResult> ShowDialogAsync(Func<ContentDialog> build) =>
        Dispatcher.UIThread.InvokeAsync(() => build().ShowAsync());

    private static async Task OnUiAsync(Action action) => await Dispatcher.UIThread.InvokeAsync(action);

    private static void OnUi(Action action) => Dispatcher.UIThread.Post(() => action());
}
