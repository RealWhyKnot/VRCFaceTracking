using System.Text.Json.Serialization;

namespace VRCFaceTracking.Models;

internal sealed class GithubContributor
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("contributions")]
    public int Contributions { get; set; }
}
