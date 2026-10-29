namespace VRCFaceTracking.Core.Contracts.Services;

public interface ILocalSettingsService
{
    Task<T?> ReadSettingAsync<T>(string key, T? defaultValue, bool forceLocal, CancellationToken cancellationToken);

    Task SaveSettingAsync<T>(string key, T value, bool forceLocal, CancellationToken cancellationToken);

    Task SaveAsync(object target, CancellationToken cancellationToken);
    Task LoadAsync(object target, CancellationToken cancellationToken);

    Task FlushAsync(CancellationToken cancellationToken);
}
