using VRCFaceTracking.Core.Contracts.Services;

namespace VRCFaceTracking.Perf.Tests;

internal sealed class NullSettings : ILocalSettingsService
{
    public Task<T?> ReadSettingAsync<T>(string key, T? defaultValue, bool forceLocal, CancellationToken cancellationToken) => Task.FromResult(defaultValue);
    public Task SaveSettingAsync<T>(string key, T value, bool forceLocal, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SaveAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task LoadAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
