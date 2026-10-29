using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Helpers;
using VRCFaceTracking.Models;

namespace VRCFaceTracking.Services;

internal sealed partial class LocalSettingsService : ILocalSettingsService, IDisposable
{
    private const string DefaultApplicationDataFolder = "VRCFaceTracking/ApplicationData";
    private const string DefaultLocalSettingsFile = "LocalSettings.json";

    private readonly IFileService _fileService;
    private readonly LocalSettingsOptions _options;
    private readonly ILogger<LocalSettingsService> _logger;

    private readonly string _localApplicationData = Core.Utils.PersistentDataDirectory;
    private readonly string _applicationDataFolder;
    private readonly string _localSettingsFile;

    private IDictionary<string, object> _settings;

    private volatile bool _isInitialized;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private readonly Lock _settingsLock = new();

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private long _saveGeneration;
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    public LocalSettingsService(IFileService fileService, IOptions<LocalSettingsOptions> options, ILogger<LocalSettingsService> logger)
    {
        _fileService = fileService;
        _options = options.Value;
        _logger = logger;

        _applicationDataFolder = Path.Combine(_localApplicationData, _options.ApplicationDataFolder ?? DefaultApplicationDataFolder);
        _localSettingsFile = _options.LocalSettingsFile ?? DefaultLocalSettingsFile;

        _settings = new Dictionary<string, object>(StringComparer.Ordinal);
    }

    public void Dispose()
    {
        _initSemaphore.Dispose();
        _semaphore.Dispose();
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
            {
                return;
            }
            await InitializeCoreAsync(cancellationToken);
            _isInitialized = true;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        string backupFile = _localSettingsFile + ".bak";
        string mainPath = Path.Combine(_applicationDataFolder, _localSettingsFile);
        string backupPath = Path.Combine(_applicationDataFolder, backupFile);

        IDictionary<string, object>? loaded = await Task.Run(() => TestSettingsFileRead(_localSettingsFile), cancellationToken);
        if (loaded == null)
        {
            LogPrimarySettingsCorrupt(_logger);
            loaded = await Task.Run(() => TestSettingsFileRead(backupFile), cancellationToken);
            if (loaded != null)
            {
                LogRestoringPrimarySettingsSessionBackup(_logger);
                Directory.CreateDirectory(_applicationDataFolder);
                File.Copy(backupPath, mainPath, overwrite: true);
            }
        }

        _settings = loaded ?? new Dictionary<string, object>(StringComparer.Ordinal);

        if (loaded == null)
        {
            LogRestoringDefaultSettings(_logger);
            await _fileService.SaveAsync(_applicationDataFolder, _localSettingsFile, _settings, cancellationToken);
        }

        if (File.Exists(mainPath))
        {
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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public async Task<T?> ReadSettingAsync<T>(string key, T? defaultValue, bool forceLocal, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);

        object? obj;
        bool found;
        lock (_settingsLock)
        {
            found = _settings != null && _settings.TryGetValue(key, out _);
            obj = found ? _settings![key] : null;
        }
        return found ? await ParseSettingAsync(key, obj, defaultValue, cancellationToken) : defaultValue;
    }

    private async Task<T?> ParseSettingAsync<T>(string key, object? raw, T? defaultValue, CancellationToken cancellationToken)
    {
        try
        {
            if (raw is not string json)
            {
                LogSettingHoldsInsteadStringUsing(_logger, key, raw?.GetType().Name ?? "null");
                return defaultValue;
            }
            return await JsonText.ToObjectAsync<T>(json, cancellationToken);
        }
        catch (JsonException e)
        {
            LogSettingCouldNotParsedUsing(_logger, key, e.Message);
            return defaultValue;
        }
    }

    public async Task SaveSettingAsync<T>(string key, T value, bool forceLocal, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);

        string json = await JsonText.StringifyAsync(value, cancellationToken);
        lock (_settingsLock)
        {
            _settings[key] = json;
        }

        _ = FlushAfterDebounceAsync(Interlocked.Increment(ref _saveGeneration), CancellationToken.None);
    }

    public async Task LoadAsync(object target, CancellationToken cancellationToken)
    {
        Type type = target.GetType();
        PropertyInfo[] properties = type.GetProperties();

        foreach (PropertyInfo property in properties)
        {
            object[] attributes = property.GetCustomAttributes(typeof(SavedSettingAttribute), inherit: false);

            if (attributes.Length <= 0)
            {
                continue;
            }

            var savedSettingAttribute = (SavedSettingAttribute)attributes[0];
            string settingName = savedSettingAttribute.SettingName;
            object? defaultValue = savedSettingAttribute.DefaultValue;

            object? setting = await ReadSettingAsync(settingName, defaultValue, savedSettingAttribute.ForceLocal, cancellationToken);
            object? convertedSetting;
            try
            {
                convertedSetting = Convert.ChangeType(setting, property.PropertyType, CultureInfo.InvariantCulture);
            }
            catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
            {
                convertedSetting = defaultValue;
            }

            property.SetValue(target, convertedSetting);
        }
    }

    public async Task SaveAsync(object target, CancellationToken cancellationToken)
    {
        Type type = target.GetType();
        PropertyInfo[] properties = type.GetProperties();

        foreach (PropertyInfo property in properties)
        {
            object[] attributes = property.GetCustomAttributes(typeof(SavedSettingAttribute), inherit: false);

            if (attributes.Length <= 0)
            {
                continue;
            }

            var savedSettingAttribute = (SavedSettingAttribute)attributes[0];
            string settingName = savedSettingAttribute.SettingName;

            await SaveSettingAsync(settingName, property.GetValue(target), savedSettingAttribute.ForceLocal, cancellationToken);
        }

        await FlushAsync(cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (!_isInitialized)
        {
            return;
        }

        Interlocked.Increment(ref _saveGeneration);
        await WriteSettingsAsync(cancellationToken);
    }

    private async Task FlushAfterDebounceAsync(long generation, CancellationToken cancellationToken)
    {
        await Task.Delay(Debounce, cancellationToken);
        if (Interlocked.Read(ref _saveGeneration) == generation)
        {
            await WriteSettingsAsync(cancellationToken);
        }
    }

    private async Task WriteSettingsAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            Dictionary<string, object> snapshot;
            lock (_settingsLock)
            {
                snapshot = new Dictionary<string, object>(_settings, StringComparer.Ordinal);
            }
            await _fileService.SaveAsync(_applicationDataFolder, _localSettingsFile, snapshot, cancellationToken);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogFailedSaveSettings(_logger, e);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Primary settings is was corrupt")]
    private static partial void LogPrimarySettingsCorrupt(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restoring primary settings from session backup")]
    private static partial void LogRestoringPrimarySettingsSessionBackup(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restoring default settings")]
    private static partial void LogRestoringDefaultSettings(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Setting {Key} holds a {Type} instead of a string; using its default")]
    private static partial void LogSettingHoldsInsteadStringUsing(ILogger logger, string key, string type);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Setting {Key} could not be parsed; using its default: {Message}")]
    private static partial void LogSettingCouldNotParsedUsing(ILogger logger, string key, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to save settings")]
    private static partial void LogFailedSaveSettings(ILogger logger, Exception exception);
}
