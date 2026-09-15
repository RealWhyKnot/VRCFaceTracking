using System.Net.Http.Headers;
using System.Text.Json;
using VRCFaceTracking.Core.Helpers;
using VRCFaceTracking.Models;

namespace VRCFaceTracking.Services;

public class GithubService
{
    private static readonly Lazy<HttpClient> Client = new(() =>
    {
        var client = HappyEyeballsHttp.CreateHttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VRCFaceTracking", "1.0"));
        return client;
    });

    public List<GithubContributor> GetBundledContributors()
    {
        try
        {
            var uri = new Uri("avares://VRCFaceTracking.Avalonia/Assets/contributors.json");
            if (!Avalonia.Platform.AssetLoader.Exists(uri))
            {
                return [];
            }
            using var stream = Avalonia.Platform.AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<List<GithubContributor>>(reader.ReadToEnd()) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public async Task<List<GithubContributor>> GetContributors(string repo)
    {
        try
        {
            var response = await Client.Value.GetAsync($"https://api.github.com/repos/{repo}/contributors");
            if (!response.IsSuccessStatusCode)
            {
                return new List<GithubContributor>();
            }
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<GithubContributor>>(content) ?? new List<GithubContributor>();
        }
        catch (Exception)
        {
            return new List<GithubContributor>();
        }
    }
}