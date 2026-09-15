using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Helpers;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Models;

namespace VRCFaceTracking.Services;

public class LocalSettingsService : ILocalSettingsService
{
    private const string _defaultApplicationDataFolder = "VRCFaceTracking/ApplicationData";
    private const string _defaultLocalSettingsFile = "LocalSettings.json";

    private readonly IFileService _fileService;
    private readonly LocalSettingsOptions _options;
    private readonly ILogger<LocalSettingsService> _logger;

    private readonly string _localApplicationData = Core.Utils.PersistentDataDirectory;
    private readonly string _applicationDataFolder;
    private readonly string _localSettingsFile;

    private IDictionary<string, object> _settings;

    private volatile bool _isInitialized;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private readonly object _settingsLock = new();

    // Save debouncing
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly object _ctsLock = new();
    private CancellationTokenSource? _cts = new();
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    public LocalSettingsService(IFileService fileService, IOptions<LocalSettingsOptions> options, ILogger<LocalSettingsService> logger)
    {
        _fileService = fileService;
        _options = options.Value;
        _logger = logger;

        _applicationDataFolder = Path.Combine(_localApplicationData, _options.ApplicationDataFolder ?? _defaultApplicationDataFolder);
        _localSettingsFile = _options.LocalSettingsFile ?? _defaultLocalSettingsFile;

        _settings = new Dictionary<string, object>();
    }

    private async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        await _initSemaphore.WaitAsync();
        try
        {
            if (_isInitialized)
            {
                return;
            }
            await InitializeCoreAsync();
            _isInitialized = true;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    private async Task InitializeCoreAsync()
    {
        var backupFile = _localSettingsFile + ".bak";
        var mainPath = Path.Combine(_applicationDataFolder, _localSettingsFile);
        var backupPath = Path.Combine(_applicationDataFolder, backupFile);

        var loaded = await Task.Run(() => TestSettingsFileRead(_localSettingsFile));
        if (loaded == null)
        {
            // If our primary settings file is unreadable or corrupt
            _logger.LogWarning("Primary settings is was corrupt");
            loaded = await Task.Run(() => TestSettingsFileRead(backupFile));
            if (loaded != null)
            {
                // But our backup settings file isn't corrupt then restore it
                _logger.LogWarning("Restoring primary settings from session backup");
                Directory.CreateDirectory(_applicationDataFolder);
                File.Copy(backupPath, mainPath, overwrite: true);
            }
        }

        _settings = loaded ?? new Dictionary<string, object>();

        if (loaded == null)
        {
            // Neither file was usable. Restore defaults
            _logger.LogWarning("Restoring default settings");
            await _fileService.Save(_applicationDataFolder, _localSettingsFile, _settings);
        }

        if (File.Exists(mainPath))
        {
            // Copy current main file to backup file
            Directory.CreateDirectory(_applicationDataFolder);
            File.Copy(mainPath, backupPath, overwrite: true);
        }
    }

    private IDictionary<string, object>? TestSettingsFileRead(string fileName)
    {
        try
        {
            return _fileService.Read<IDictionary<string, object>>(_applicationDataFolder, fileName);
        }
        catch
        {
            return null;
        }
    }

    public async Task<T?> ReadSettingAsync<T>(string key, T? defaultValue = default, bool forceLocal = false)
    {
        await InitializeAsync();

        object? obj;
        bool found;
        lock (_settingsLock)
        {
            found = _settings != null && _settings.TryGetValue(key, out obj);
            obj = found ? _settings![key] : null;
        }
        if (found)
        {
            return await ParseSettingAsync(key, obj, defaultValue);
        }

        return defaultValue;
    }

    private async Task<T?> ParseSettingAsync<T>(string key, object? raw, T? defaultValue)
    {
        try
        {
            if (raw is not string json)
            {
                _logger.LogWarning("Setting {Key} holds a {Type} instead of a string; using its default", key, raw?.GetType().Name ?? "null");
                return defaultValue;
            }
            return await Json.ToObjectAsync<T>(json);
        }
        catch (Exception e)
        {
            _logger.LogWarning("Setting {Key} could not be parsed; using its default: {Message}", key, e.Message);
            return defaultValue;
        }
    }

    public async Task SaveSettingAsync<T>(string key, T value, bool forceLocal = false)
    {
        await InitializeAsync();

        var json = await Json.StringifyAsync(value);
        lock (_settingsLock)
        {
            _settings[key] = json;
        }

        _ = FlushSaveSettings();
    }

    public async Task Load(object instance)
    {
        var type = instance.GetType();
        var properties = type.GetProperties();

        foreach (var property in properties)
        {
            var attributes = property.GetCustomAttributes(typeof(SavedSettingAttribute), false);

            if (attributes.Length <= 0)
            {
                continue;
            }

            var savedSettingAttribute = (SavedSettingAttribute)attributes[0];
            var settingName = savedSettingAttribute.GetName();
            var defaultValue = savedSettingAttribute.Default();

            var setting = await ReadSettingAsync(settingName, defaultValue, savedSettingAttribute.ForceLocal());
            object? convertedSetting;
            try
            {
                convertedSetting = Convert.ChangeType(setting, property.PropertyType);
            }
            catch
            {
                convertedSetting = defaultValue;
            }

            property.SetValue(instance, convertedSetting);
        }
    }

    public async Task Save(object instance)
    {
        var type = instance.GetType();
        var properties = type.GetProperties();

        foreach (var property in properties)
        {
            var attributes = property.GetCustomAttributes(typeof(SavedSettingAttribute), false);

            if (attributes.Length <= 0)
            {
                continue;
            }

            var savedSettingAttribute = (SavedSettingAttribute)attributes[0];
            var settingName = savedSettingAttribute.GetName();

            await SaveSettingAsync(settingName, property.GetValue(instance), savedSettingAttribute.ForceLocal());
        }

        await FlushNowAsync();
    }

    private async Task FlushNowAsync()
    {
        if (!_isInitialized)
        {
            return;
        }

        lock (_ctsLock)
        {
            _cts?.Cancel();
        }

        await _semaphore.WaitAsync();
        try
        {
            Dictionary<string, object> snapshot;
            lock (_settingsLock)
            {
                snapshot = new Dictionary<string, object>(_settings);
            }
            await _fileService.Save(_applicationDataFolder, _localSettingsFile, snapshot);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to save settings");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task FlushSaveSettings()
    {
        var cts = new CancellationTokenSource();
        lock (_ctsLock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = cts;
        }

        try
        {
            await Task.Delay(Debounce, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Newer save req came in. Skip this one and let the new one do the write.
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        await _semaphore.WaitAsync();
        try
        {
            Dictionary<string, object> snapshot;
            lock (_settingsLock)
            {
                snapshot = new Dictionary<string, object>(_settings);
            }
            await _fileService.Save(_applicationDataFolder, _localSettingsFile, snapshot);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to save settings");
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
