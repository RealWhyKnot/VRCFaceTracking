using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VRCFaceTracking.Core.Helpers;
using VRCFaceTracking.Core.Models;

namespace VRCFaceTracking.Core.Services;

public class ModuleInstaller
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

    // Move a directory using just Copy and Remove as MoveDirectory is not usable across drives
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

        // Get files recursively and preserve directory structure
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var path = Path.GetDirectoryName(file);
            var newPath = path?.Replace(source, dest);
            if (newPath == null)
            {
                continue;
            }

            Directory.CreateDirectory(newPath);
            File.Copy(file, Path.Combine(newPath, Path.GetFileName(file)), true);
        }

        // Now we delete the source directory
        Directory.Delete(source, true);
    }

    private static async Task DownloadModuleToFile(TrackingModuleMetadata moduleMetadata, string filePath, string md5Hash = null)
    {
        using var client = HappyEyeballsHttp.CreateHttpClient();

        var response = await client.GetAsync(moduleMetadata.DownloadUrl);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsByteArrayAsync();
        if (!string.IsNullOrEmpty(md5Hash))
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(content);
            var hashStr = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();

            if (!string.Equals(hashStr, md5Hash.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"MD5 hash mismatch. Expected {md5Hash}, got {hashStr}");
            }
        }

        var tempPath = filePath + ".tmp";
        await File.WriteAllBytesAsync(tempPath, content);
        File.Move(tempPath, filePath, true);
    }

    /* Removes the 'downloaded from the internet' attribute from a module
     * @param DLL file path
     * @return error; if true then the module should be skipped
     */
    [SupportedOSPlatform("windows")]
    private bool RemoveZoneIdentifier(string path)
    {
        var zoneFile = path + ":Zone.Identifier";

        if (Utils.GetFileAttributes(zoneFile) == 0xffffffff) // INVALID_FILE_ATTRIBUTES
            //zone file doesn't exist, everything's good
            return false;

        if (Utils.DeleteFile(zoneFile))
            _logger.LogDebug("Removing the downloaded file identifier from " + path);
        else
        {
            _logger.LogError("Couldn't removed the 'file downloaded' mark from the " + path + " module! Please unblock the file manually");
            return true;
        }

        return false;
    }

    private string TryFindModuleDll(string moduleDirectory, TrackingModuleMetadata moduleMetadata)
    {
        // Attempt to find the first DLL. If there's more than one, try find the one with the same name as the module
        var dllFiles = Directory.GetFiles(moduleDirectory, "*.dll");

        switch (dllFiles.Length)
        {
            case 0:
                return null;
            // If there's only one, just return it
            case 1:
                return Path.GetFileName(dllFiles[0]);
        }

        // Else we'll try find the one with the closest name to the module using Levenshtein distance
        var targetFileName = Path.GetFileNameWithoutExtension(moduleMetadata.DownloadUrl);
        var dllFile = dllFiles.Select(x => new { FileName = Path.GetFileNameWithoutExtension(x), Distance = LevenshteinDistance.Calculate(targetFileName, Path.GetFileNameWithoutExtension(x)) }).MinBy(x => x.Distance);

        if (dllFile == null)
        {
            _logger.LogError(
                "Module {module} has no .dll file name specified and no .dll files were found in the extracted zip",
                moduleMetadata.ModuleId);
            return null;
        }

        _logger.LogDebug("Module {module} didn't specify a target dll, and contained multiple. Using {dll} as its distance of {distance} was closest to the module name",
            moduleMetadata.ModuleId, dllFile.FileName, dllFile.Distance);
        return Path.GetFileName(dllFile.FileName);
    }

    public async Task<string> InstallLocalModule(string zipPath)
    {
        // First, we copy the zip to our custom libs directory
        var fileName = Path.GetFileName(zipPath);
        var newZipPath = Path.Combine(_customLibsDirectory, fileName);
        File.Copy(zipPath, newZipPath, true);

        // Second, we unzip it 
        var tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(zipPath));
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, true);
        }
        Directory.CreateDirectory(tempDirectory);
        ZipFile.ExtractToDirectory(newZipPath, tempDirectory);
        File.Delete(newZipPath);

        // Now, we need to find the module.json file and deserialize it
        var moduleJsonPath = Path.Combine(tempDirectory, "module.json");
        if (!File.Exists(moduleJsonPath))
        {
            _logger.LogError("Module {module} does not contain a module.json file", fileName);
            Directory.Delete(tempDirectory, true);
            return null;
        }

        var moduleMetadata = await Json.ToObjectAsync<TrackingModuleMetadata>(await File.ReadAllTextAsync(moduleJsonPath));
        if (moduleMetadata == null)
        {
            _logger.LogError("Module {module} contains an invalid module.json file", fileName);
            Directory.Delete(tempDirectory, true);
            return null;
        }
        moduleMetadata.IsLocal = true;

        // Now we move to a directory named after the module id and delete the temp directory
        var moduleDirectory = Path.Combine(_customLibsDirectory, moduleMetadata.ModuleId.ToString());
        if (Directory.Exists(moduleDirectory))
        {
            Directory.Delete(moduleDirectory, true);
        }

        MoveDirectory(tempDirectory, moduleDirectory);

        // Now we need to find the module's dll
        moduleMetadata.DllFileName ??= TryFindModuleDll(moduleDirectory, moduleMetadata);
        if (moduleMetadata.DllFileName == null)
        {
            _logger.LogError("Module {module} has no .dll file name specified and no .dll files were found in the extracted zip", moduleMetadata.ModuleId);
            return null;
        }

        // Now we write the module.json file to the module directory
        await File.WriteAllTextAsync(Path.Combine(moduleDirectory, "module.json"), JsonConvert.SerializeObject(moduleMetadata, Formatting.Indented));

        // Finally, we return the module's dll file name
        return Path.Combine(moduleDirectory, moduleMetadata.DllFileName);
    }

    public async Task<string> InstallRemoteModule(TrackingModuleMetadata moduleMetadata)
    {
        var moduleDirectory = Path.Combine(_customLibsDirectory, moduleMetadata.ModuleId.ToString());
        var stagingDirectory = Path.Combine(_customLibsDirectory, ".staging-" + Path.GetRandomFileName());
        var stagedModuleDirectory = Path.Combine(stagingDirectory, "module");
        var previousDirectory = Path.Combine(stagingDirectory, "previous");
        string? tempDirectory = null;

        try
        {
            try
            {
                Directory.CreateDirectory(stagedModuleDirectory);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to provision staging dir for {module}", moduleMetadata.ModuleName);
                return null;
            }

            var downloadExtension = Path.GetExtension(moduleMetadata.DownloadUrl);
            if (downloadExtension != ".dll")
            {
                _logger.LogDebug($"Provisioning temp download dir for {moduleMetadata.ModuleName}");
                try
                {
                    tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                    if (Directory.Exists(tempDirectory))
                    {
                        Directory.Delete(tempDirectory, true);
                    }

                    Directory.CreateDirectory(tempDirectory);
                }
                catch (Exception e)
                {
                    _logger.LogError($"Failed to provision download dir for {moduleMetadata.ModuleName}. {e}");
                    return null;
                }

                _logger.LogInformation($"Downloading {moduleMetadata.ModuleName} to temp dir {tempDirectory}");
                string tempZipPath;
                try
                {
                    tempZipPath = Path.Combine(tempDirectory, "module.zip");
                    await DownloadModuleToFile(moduleMetadata, tempZipPath, moduleMetadata.FileHash);
                }
                catch (Exception e)
                {
                    _logger.LogError($"Failed to download module data for {moduleMetadata.ModuleName}. {e}");
                    return null;
                }

                _logger.LogInformation($"Extracting zip to {tempDirectory}");
                try
                {
                    ZipFile.ExtractToDirectory(tempZipPath, tempDirectory);
                    File.Delete(tempZipPath);
                }
                catch (Exception e)
                {
                    _logger.LogError($"Failed to extract {moduleMetadata.ModuleName}. {e}");
                    return null;
                }

                _logger.LogInformation($"Moving extracted files for {moduleMetadata.ModuleName} from {tempDirectory} to {stagedModuleDirectory}");
                try
                {
                    MoveDirectory(tempDirectory, stagedModuleDirectory);
                }
                catch (Exception e)
                {
                    _logger.LogError($"Failed to move files for {moduleMetadata.ModuleName}. {e}");
                    return null;
                }

                if (OperatingSystem.IsWindows())
                {
                    foreach (var dll in Directory.GetFiles(stagedModuleDirectory, "*.dll", SearchOption.AllDirectories))
                    {
                        RemoveZoneIdentifier(dll);
                    }
                }

                moduleMetadata.DllFileName ??= TryFindModuleDll(stagedModuleDirectory, moduleMetadata);
                if (moduleMetadata.DllFileName == null)
                {
                    _logger.LogError("Module {module} has no .dll file name specified and no .dll files were found in the extracted zip", moduleMetadata.ModuleId);
                    return null;
                }
            }
            else
            {
                moduleMetadata.DllFileName = string.IsNullOrEmpty(moduleMetadata.DllFileName)
                    ? Path.GetFileName(moduleMetadata.DownloadUrl)
                    : moduleMetadata.DllFileName;

                var dllPath = Path.Combine(
                    stagedModuleDirectory,
                    moduleMetadata.DllFileName);

                try
                {
                    await DownloadModuleToFile(moduleMetadata, dllPath, moduleMetadata.FileHash);
                }
                catch (Exception e)
                {
                    _logger.LogError($"Failed to download module data for {moduleMetadata.ModuleName}. {e}");
                    return null;
                }

                _logger.LogDebug("Downloaded module {module} to {dllPath}", moduleMetadata.ModuleId, dllPath);
            }

            var moduleJsonPath = Path.Combine(stagedModuleDirectory, "module.json");
            await File.WriteAllTextAsync(moduleJsonPath, JsonConvert.SerializeObject(moduleMetadata, Formatting.Indented));

            if (!ReplaceModuleDirectory(moduleMetadata, stagedModuleDirectory, moduleDirectory, previousDirectory))
            {
                return null;
            }
        }
        finally
        {
            DeleteDirectory(tempDirectory);
            if (!Directory.Exists(previousDirectory) || Directory.Exists(moduleDirectory))
            {
                DeleteDirectory(stagingDirectory);
            }
        }

        _logger.LogInformation("Installed module {module} to {moduleDirectory}", moduleMetadata.ModuleId, moduleDirectory);

        return Path.Combine(moduleDirectory, moduleMetadata.DllFileName);
    }

    private bool ReplaceModuleDirectory(TrackingModuleMetadata moduleMetadata, string stagedModuleDirectory, string moduleDirectory, string previousDirectory)
    {
        try
        {
            if (Directory.Exists(moduleDirectory))
            {
                Directory.Move(moduleDirectory, previousDirectory);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not move the installed copy of {module} aside, so it was left in place", moduleMetadata.ModuleId);
            return false;
        }

        try
        {
            Directory.Move(stagedModuleDirectory, moduleDirectory);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not move the new copy of {module} into {moduleDirectory}; restoring the installed copy", moduleMetadata.ModuleId, moduleDirectory);
        }

        try
        {
            if (Directory.Exists(previousDirectory))
            {
                Directory.Move(previousDirectory, moduleDirectory);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not restore {module}; the installed copy is in {previousDirectory}", moduleMetadata.ModuleId, previousDirectory);
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
            Directory.Delete(directory, true);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not delete {directory}", directory);
        }
    }

    public void UninstallModule(TrackingModuleMetadata moduleMetadata)
    {
        _logger.LogDebug("Uninstalling module {module}", moduleMetadata.ModuleId);
        var moduleDirectory = Path.Combine(_customLibsDirectory, moduleMetadata.ModuleId.ToString());
        if (Directory.Exists(moduleDirectory))
        {
            try
            {
                Directory.Delete(moduleDirectory, true);
                _logger.LogInformation("Uninstalled module {module} from {moduleDirectory}", moduleMetadata.ModuleId, moduleDirectory);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to uninstall module {module} from {moduleDirectory}", moduleMetadata.ModuleId, moduleDirectory);
            }
        }
        else
        {
            _logger.LogDebug("Module {module} could not be found where it was expected in {moduleDirectory}", moduleMetadata.ModuleId, moduleDirectory);
        }
    }
}
