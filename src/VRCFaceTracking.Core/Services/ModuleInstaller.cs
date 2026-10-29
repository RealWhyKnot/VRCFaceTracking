using System.IO.Compression;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VRCFaceTracking.Core.Helpers;
using VRCFaceTracking.Core.Models;

namespace VRCFaceTracking.Core.Services;

public sealed partial class ModuleInstaller
{
    private readonly ILogger<ModuleInstaller> _logger;
    private readonly string _customLibsDirectory;

    public ModuleInstaller(ILogger<ModuleInstaller> logger) : this(logger, Utils.CustomLibsDirectory)
    {
    }

    internal ModuleInstaller(ILogger<ModuleInstaller> logger, string customLibsDirectory)
    {
        _logger = logger;
        _customLibsDirectory = customLibsDirectory;

        if (!Directory.Exists(_customLibsDirectory))
        {
            Directory.CreateDirectory(_customLibsDirectory);
        }
    }

    private static void MoveDirectory(string source, string dest)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(dest))
        {
            return;
        }

        if (!Directory.Exists(dest))
        {
            Directory.CreateDirectory(dest);
        }

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string? path = Path.GetDirectoryName(file);
            string? newPath = path?.Replace(source, dest, StringComparison.Ordinal);
            if (newPath == null)
            {
                continue;
            }

            Directory.CreateDirectory(newPath);
            File.Copy(file, Path.Combine(newPath, Path.GetFileName(file)), overwrite: true);
        }

        Directory.Delete(source, recursive: true);
    }

    private static async Task DownloadModuleToFileAsync(TrackingModuleMetadata moduleMetadata, string filePath, CancellationToken cancellationToken)
    {
        using HttpClient client = HappyEyeballsHttp.CreateHttpClient();

        using HttpResponseMessage response = await client.GetAsync(moduleMetadata.DownloadUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        string tempPath = filePath + ".tmp";
        await File.WriteAllBytesAsync(tempPath, content, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, filePath, overwrite: true);
    }

    [SupportedOSPlatform("windows")]
    private bool RemoveZoneIdentifier(string path)
    {
        string zoneFile = path + ":Zone.Identifier";

        if (Utils.GetFileAttributes(zoneFile) == 0xffffffff)
        {
            return false;
        }

        if (Utils.DeleteFile(zoneFile))
        {
            LogRemovingDownloadedFileIdentifier(_logger, path);
        }
        else
        {
            LogCouldNotRemoveDownloadedMark(_logger, path);
            return true;
        }

        return false;
    }

    private string? TryFindModuleDll(string moduleDirectory, TrackingModuleMetadata moduleMetadata)
    {
        string[] dllFiles = Directory.GetFiles(moduleDirectory, "*.dll");

        if (dllFiles.Length == 0)
        {
            return null;
        }

        if (dllFiles.Length == 1)
        {
            return Path.GetFileName(dllFiles[0]);
        }

        string targetFileName = Path.GetFileNameWithoutExtension(moduleMetadata.DownloadUrl?.OriginalString) ?? string.Empty;
        var dllFile = dllFiles.Select(x => new { FileName = Path.GetFileNameWithoutExtension(x), Distance = LevenshteinDistance.Calculate(targetFileName, Path.GetFileNameWithoutExtension(x)) }).MinBy(x => x.Distance);

        if (dllFile == null)
        {
            LogModuleNoDllFileName(_logger, moduleMetadata.ModuleId);
            return null;
        }

        LogModuleDidnSpecifyTargetDll(_logger, moduleMetadata.ModuleId, dllFile.FileName, dllFile.Distance);
        return Path.GetFileName(dllFile.FileName);
    }

    public async Task<string?> InstallLocalModuleAsync(string zipPath, CancellationToken cancellationToken)
    {
        string fileName = Path.GetFileName(zipPath);
        string newZipPath = Path.Combine(_customLibsDirectory, fileName);
        File.Copy(zipPath, newZipPath, overwrite: true);

        string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(zipPath));
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
        Directory.CreateDirectory(tempDirectory);
        await ZipFile.ExtractToDirectoryAsync(newZipPath, tempDirectory, cancellationToken).ConfigureAwait(false);
        File.Delete(newZipPath);

        string moduleJsonPath = Path.Combine(tempDirectory, "module.json");
        if (!File.Exists(moduleJsonPath))
        {
            LogModuleDoesNotContainModule(_logger, fileName);
            Directory.Delete(tempDirectory, recursive: true);
            return null;
        }

        string moduleJson = await File.ReadAllTextAsync(moduleJsonPath, cancellationToken).ConfigureAwait(false);
        TrackingModuleMetadata? moduleMetadata = await JsonText.ToObjectAsync<TrackingModuleMetadata>(moduleJson, cancellationToken).ConfigureAwait(false);
        if (moduleMetadata == null)
        {
            LogModuleContainsInvalidModuleJson(_logger, fileName);
            Directory.Delete(tempDirectory, recursive: true);
            return null;
        }
        moduleMetadata.IsLocal = true;

        string moduleDirectory = Path.Combine(_customLibsDirectory, moduleMetadata.ModuleId.ToString());
        if (Directory.Exists(moduleDirectory))
        {
            Directory.Delete(moduleDirectory, recursive: true);
        }

        MoveDirectory(tempDirectory, moduleDirectory);

        moduleMetadata.DllFileName ??= TryFindModuleDll(moduleDirectory, moduleMetadata);
        if (moduleMetadata.DllFileName == null)
        {
            LogModuleNoDllFileName(_logger, moduleMetadata.ModuleId);
            return null;
        }

        await File.WriteAllTextAsync(Path.Combine(moduleDirectory, "module.json"), JsonConvert.SerializeObject(moduleMetadata, Formatting.Indented), cancellationToken).ConfigureAwait(false);

        return Path.Combine(moduleDirectory, moduleMetadata.DllFileName);
    }

    public async Task<string?> InstallRemoteModuleAsync(TrackingModuleMetadata moduleMetadata, CancellationToken cancellationToken)
    {
        string moduleDirectory = Path.Combine(_customLibsDirectory, moduleMetadata.ModuleId.ToString());
        string stagingDirectory = Path.Combine(_customLibsDirectory, ".staging-" + Path.GetRandomFileName());
        string stagedModuleDirectory = Path.Combine(stagingDirectory, "module");
        string previousDirectory = Path.Combine(stagingDirectory, "previous");
        string? dllFileName;

        try
        {
            try
            {
                Directory.CreateDirectory(stagedModuleDirectory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LogFailedProvisionStagingDir(_logger, e, moduleMetadata.ModuleName);
                return null;
            }

            dllFileName = string.Equals(Path.GetExtension(moduleMetadata.DownloadUrl?.OriginalString), ".dll", StringComparison.Ordinal)
                ? await StageDllAsync(moduleMetadata, stagedModuleDirectory, cancellationToken).ConfigureAwait(false)
                : await StageArchiveAsync(moduleMetadata, stagedModuleDirectory, cancellationToken).ConfigureAwait(false);
            if (dllFileName == null)
            {
                return null;
            }

            string moduleJsonPath = Path.Combine(stagedModuleDirectory, "module.json");
            await File.WriteAllTextAsync(moduleJsonPath, JsonConvert.SerializeObject(moduleMetadata, Formatting.Indented), cancellationToken).ConfigureAwait(false);

            if (!ReplaceModuleDirectory(moduleMetadata, stagedModuleDirectory, moduleDirectory, previousDirectory))
            {
                return null;
            }
        }
        finally
        {
            if (!Directory.Exists(previousDirectory) || Directory.Exists(moduleDirectory))
            {
                DeleteDirectory(stagingDirectory);
            }
        }

        LogInstalledModule(_logger, moduleMetadata.ModuleId, moduleDirectory);

        return Path.Combine(moduleDirectory, dllFileName);
    }

    private async Task<string?> StageDllAsync(TrackingModuleMetadata moduleMetadata, string stagedModuleDirectory, CancellationToken cancellationToken)
    {
        string dllFileName = string.IsNullOrEmpty(moduleMetadata.DllFileName)
            ? Path.GetFileName(moduleMetadata.DownloadUrl?.OriginalString) ?? string.Empty
            : moduleMetadata.DllFileName;
        moduleMetadata.DllFileName = dllFileName;

        string dllPath = Path.Combine(stagedModuleDirectory, dllFileName);

        try
        {
            await DownloadModuleToFileAsync(moduleMetadata, dllPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (IsDownloadFailure(e, cancellationToken))
        {
            LogFailedDownloadModuleData(_logger, e, moduleMetadata.ModuleName, e.Message);
            return null;
        }

        LogDownloadedModule(_logger, moduleMetadata.ModuleId, dllPath);
        return dllFileName;
    }

    private async Task<string?> StageArchiveAsync(TrackingModuleMetadata moduleMetadata, string stagedModuleDirectory, CancellationToken cancellationToken)
    {
        LogProvisioningTempDownloadDir(_logger, moduleMetadata.ModuleName);
        string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            if (!await TryDownloadArchiveAsync(moduleMetadata, tempDirectory, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            LogMovingExtractedFiles(_logger, moduleMetadata.ModuleName, tempDirectory, stagedModuleDirectory);
            try
            {
                MoveDirectory(tempDirectory, stagedModuleDirectory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LogFailedMoveModuleFiles(_logger, e, moduleMetadata.ModuleName, e.Message);
                return null;
            }
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (string dll in Directory.GetFiles(stagedModuleDirectory, "*.dll", SearchOption.AllDirectories))
            {
                RemoveZoneIdentifier(dll);
            }
        }

        moduleMetadata.DllFileName ??= TryFindModuleDll(stagedModuleDirectory, moduleMetadata);
        if (moduleMetadata.DllFileName == null)
        {
            LogModuleNoDllFileName(_logger, moduleMetadata.ModuleId);
        }

        return moduleMetadata.DllFileName;
    }

    private async Task<bool> TryDownloadArchiveAsync(TrackingModuleMetadata moduleMetadata, string tempDirectory, CancellationToken cancellationToken)
    {
        try
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }

            Directory.CreateDirectory(tempDirectory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogFailedProvisionDownloadDir(_logger, e, moduleMetadata.ModuleName, e.Message);
            return false;
        }

        LogDownloadingTempDir(_logger, moduleMetadata.ModuleName, tempDirectory);
        string tempZipPath = Path.Combine(tempDirectory, "module.zip");
        try
        {
            await DownloadModuleToFileAsync(moduleMetadata, tempZipPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (IsDownloadFailure(e, cancellationToken))
        {
            LogFailedDownloadModuleData(_logger, e, moduleMetadata.ModuleName, e.Message);
            return false;
        }

        LogExtractingZip(_logger, tempDirectory);
        try
        {
            await ZipFile.ExtractToDirectoryAsync(tempZipPath, tempDirectory, cancellationToken).ConfigureAwait(false);
            File.Delete(tempZipPath);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            LogFailedExtractModule(_logger, e, moduleMetadata.ModuleName, e.Message);
            return false;
        }

        return true;
    }

    private static bool IsDownloadFailure(Exception e, CancellationToken cancellationToken) =>
        e is HttpRequestException or InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or UriFormatException
        || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private bool ReplaceModuleDirectory(TrackingModuleMetadata moduleMetadata, string stagedModuleDirectory, string moduleDirectory, string previousDirectory)
    {
        try
        {
            if (Directory.Exists(moduleDirectory))
            {
                Directory.Move(moduleDirectory, previousDirectory);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogCouldNotMoveInstalledCopy(_logger, e, moduleMetadata.ModuleId);
            return false;
        }

        try
        {
            Directory.Move(stagedModuleDirectory, moduleDirectory);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogCouldNotMoveNewCopy(_logger, e, moduleMetadata.ModuleId, moduleDirectory);
        }

        try
        {
            if (Directory.Exists(previousDirectory))
            {
                Directory.Move(previousDirectory, moduleDirectory);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogCouldNotRestoreInstalledCopy(_logger, e, moduleMetadata.ModuleId, previousDirectory);
        }

        return false;
    }

    private void DeleteDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogCouldNotDelete(_logger, e, directory);
        }
    }

    public void UninstallModule(TrackingModuleMetadata moduleMetadata)
    {
        LogUninstallingModule(_logger, moduleMetadata.ModuleId);
        string moduleDirectory = Path.Combine(_customLibsDirectory, moduleMetadata.ModuleId.ToString());
        if (Directory.Exists(moduleDirectory))
        {
            try
            {
                Directory.Delete(moduleDirectory, recursive: true);
                LogUninstalledModule(_logger, moduleMetadata.ModuleId, moduleDirectory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LogFailedUninstallModule(_logger, e, moduleMetadata.ModuleId, moduleDirectory);
            }
        }
        else
        {
            LogModuleCouldNotFoundWhere(_logger, moduleMetadata.ModuleId, moduleDirectory);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Module {Module} has no .dll file name specified and no .dll files were found in the extracted zip")]
    private static partial void LogModuleNoDllFileName(ILogger logger, Guid module);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Module {Module} didn't specify a target dll, and contained multiple. Using {Dll} as its distance of {Distance} was closest to the module name")]
    private static partial void LogModuleDidnSpecifyTargetDll(ILogger logger, Guid module, string dll, int distance);

    [LoggerMessage(Level = LogLevel.Error, Message = "Module {Module} does not contain a module.json file")]
    private static partial void LogModuleDoesNotContainModule(ILogger logger, string module);

    [LoggerMessage(Level = LogLevel.Error, Message = "Module {Module} contains an invalid module.json file")]
    private static partial void LogModuleContainsInvalidModuleJson(ILogger logger, string module);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to provision staging dir for {Module}")]
    private static partial void LogFailedProvisionStagingDir(ILogger logger, Exception exception, string module);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Provisioning temp download dir for {ModuleName}")]
    private static partial void LogProvisioningTempDownloadDir(ILogger logger, string moduleName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloading {ModuleName} to temp dir {TempDirectory}")]
    private static partial void LogDownloadingTempDir(ILogger logger, string moduleName, string tempDirectory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracting zip to {TempDirectory}")]
    private static partial void LogExtractingZip(ILogger logger, string tempDirectory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Moving extracted files for {ModuleName} from {TempDirectory} to {StagedModuleDirectory}")]
    private static partial void LogMovingExtractedFiles(ILogger logger, string moduleName, string tempDirectory, string stagedModuleDirectory);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Downloaded module {Module} to {DllPath}")]
    private static partial void LogDownloadedModule(ILogger logger, Guid module, string dllPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Installed module {Module} to {ModuleDirectory}")]
    private static partial void LogInstalledModule(ILogger logger, Guid module, string moduleDirectory);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not move the installed copy of {Module} aside, so it was left in place")]
    private static partial void LogCouldNotMoveInstalledCopy(ILogger logger, Exception exception, Guid module);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not move the new copy of {Module} into {ModuleDirectory}; restoring the installed copy")]
    private static partial void LogCouldNotMoveNewCopy(ILogger logger, Exception exception, Guid module, string moduleDirectory);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not restore {Module}; the installed copy is in {PreviousDirectory}")]
    private static partial void LogCouldNotRestoreInstalledCopy(ILogger logger, Exception exception, Guid module, string previousDirectory);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete {Directory}")]
    private static partial void LogCouldNotDelete(ILogger logger, Exception exception, string directory);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Uninstalling module {Module}")]
    private static partial void LogUninstallingModule(ILogger logger, Guid module);

    [LoggerMessage(Level = LogLevel.Information, Message = "Uninstalled module {Module} from {ModuleDirectory}")]
    private static partial void LogUninstalledModule(ILogger logger, Guid module, string moduleDirectory);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to uninstall module {Module} from {ModuleDirectory}")]
    private static partial void LogFailedUninstallModule(ILogger logger, Exception exception, Guid module, string moduleDirectory);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Module {Module} could not be found where it was expected in {ModuleDirectory}")]
    private static partial void LogModuleCouldNotFoundWhere(ILogger logger, Guid module, string moduleDirectory);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Removing the downloaded file identifier from {Path}")]
    private static partial void LogRemovingDownloadedFileIdentifier(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't remove the 'file downloaded' mark from the {Path} module! Please unblock the file manually")]
    private static partial void LogCouldNotRemoveDownloadedMark(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to provision download dir for {Module}. {ErrorMessage}")]
    private static partial void LogFailedProvisionDownloadDir(ILogger logger, Exception exception, string module, string errorMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to download module data for {Module}. {ErrorMessage}")]
    private static partial void LogFailedDownloadModuleData(ILogger logger, Exception exception, string module, string errorMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to extract {Module}. {ErrorMessage}")]
    private static partial void LogFailedExtractModule(ILogger logger, Exception exception, string module, string errorMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to move files for {Module}. {ErrorMessage}")]
    private static partial void LogFailedMoveModuleFiles(ILogger logger, Exception exception, string module, string errorMessage);
}
