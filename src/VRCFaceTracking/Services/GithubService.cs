using System.Net.Http.Headers;
using System.Text.Json;
using VRCFaceTracking.Core.Helpers;
using VRCFaceTracking.Models;

namespace VRCFaceTracking.Services;

internal static class GithubService
{
    private static readonly Lazy<HttpClient> Client = new(() =>
    {
        HttpClient client = HappyEyeballsHttp.CreateHttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VRCFaceTracking", "1.0"));
        return client;
    });

    public static List<GithubContributor> GetBundledContributors()
    {
        try
        {
            var uri = new Uri("avares://VRCFaceTracking/Assets/contributors.json");
            if (!Avalonia.Platform.AssetLoader.Exists(uri))
            {
                return [];
            }
            using Stream stream = Avalonia.Platform.AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<List<GithubContributor>>(reader.ReadToEnd()) ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return [];
        }
    }

    public static async Task<List<GithubContributor>> GetContributorsAsync(string repo, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await Client.Value.GetAsync(new Uri($"https://api.github.com/repos/{repo}/contributors"), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }
            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<List<GithubContributor>>(content) ?? [];
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return [];
        }
    }
}
