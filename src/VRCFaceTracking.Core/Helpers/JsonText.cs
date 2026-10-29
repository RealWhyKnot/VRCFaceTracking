using Newtonsoft.Json;

namespace VRCFaceTracking.Core.Helpers;

public static class JsonText
{
    public static Task<T?> ToObjectAsync<T>(string value, CancellationToken cancellationToken) =>
        Task.Run(() => JsonConvert.DeserializeObject<T>(value), cancellationToken);

    public static Task<string> StringifyAsync(object? value, CancellationToken cancellationToken) =>
        Task.Run(() => JsonConvert.SerializeObject(value), cancellationToken);
}
