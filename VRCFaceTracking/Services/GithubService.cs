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