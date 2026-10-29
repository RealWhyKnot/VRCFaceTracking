using System.Text.Json.Serialization;

namespace VRCFaceTracking.Core.Updates;

public sealed class GithubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public Uri? HtmlUrl { get; set; }

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
    public IReadOnlyList<GithubReleaseAsset> Assets { get; set; } = [];
}
