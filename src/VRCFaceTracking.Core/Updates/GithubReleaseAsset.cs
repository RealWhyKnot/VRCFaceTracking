using System.Text.Json.Serialization;

namespace VRCFaceTracking.Core.Updates;

public sealed class GithubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public Uri? BrowserDownloadUrl { get; set; }
}
