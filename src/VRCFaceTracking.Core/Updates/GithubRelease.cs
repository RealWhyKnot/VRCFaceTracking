using System.Text.Json.Serialization;

namespace VRCFaceTracking.Core.Updates;

public sealed class GithubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;
}

public sealed class GithubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("prerelease")]
    public bool Prerelease
    {
        get; set;
    }

    [JsonPropertyName("draft")]
    public bool Draft
    {
        get; set;
    }

    [JsonPropertyName("assets")]
    public List<GithubReleaseAsset> Assets { get; set; } = new();
}
