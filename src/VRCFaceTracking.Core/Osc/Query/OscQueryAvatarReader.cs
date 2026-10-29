using System.Net;
using Newtonsoft.Json;

namespace VRCFaceTracking.Core.Osc.Query;

internal static class OscQueryAvatarReader
{
    internal static async Task<OscQueryNode> ReadAsync(HttpClient http, Uri avatarEndpoint, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadNodeAsync(http, avatarEndpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return await ReadPiecewiseAsync(http, avatarEndpoint, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<OscQueryNode> ReadPiecewiseAsync(HttpClient http, Uri avatarEndpoint, CancellationToken cancellationToken)
    {
        var avatarBase = new Uri(avatarEndpoint.AbsoluteUri.TrimEnd('/') + "/");
        var changeEndpoint = new Uri(avatarBase, "change");
        var parametersEndpoint = new Uri(avatarBase, "parameters");

        for (int attempt = 0; attempt < 2; attempt++)
        {
            OscQueryNode change = await ReadNodeAsync(http, changeEndpoint, cancellationToken).ConfigureAwait(false);
            string idBefore = ReadAvatarId(change);
            OscQueryNode parameters = await ReadNodeAsync(http, parametersEndpoint, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(parameters.FullPath, "/avatar/parameters", StringComparison.Ordinal) || parameters.Contents == null)
            {
                throw new JsonSerializationException("OSCQuery returned an invalid avatar parameter tree");
            }

            OscQueryNode? fullFaceTracking = await ReadOptionalNodeAsync(http, new Uri(avatarBase, "avatarfullfacetracking"), cancellationToken).ConfigureAwait(false);
            OscQueryNode? name = await ReadOptionalNameAsync(http, new Uri(avatarBase, "name"), cancellationToken).ConfigureAwait(false);

            if (!string.Equals(idBefore, ReadAvatarId(await ReadNodeAsync(http, changeEndpoint, cancellationToken).ConfigureAwait(false)), StringComparison.Ordinal))
            {
                continue;
            }

            var contents = new Dictionary<string, OscQueryNode>(StringComparer.Ordinal)
            {
                ["change"] = change,
                ["parameters"] = parameters,
            };
            if (fullFaceTracking != null)
            {
                contents["avatarfullfacetracking"] = fullFaceTracking;
            }

            if (name != null)
            {
                contents["name"] = name;
            }

            return new OscQueryNode("/avatar") { Contents = contents };
        }

        throw new JsonSerializationException("Avatar changed while reading its OSCQuery data");
    }

    private static async Task<OscQueryNode?> ReadOptionalNodeAsync(HttpClient http, Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadNodeAsync(http, uri, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static async Task<OscQueryNode?> ReadOptionalNameAsync(HttpClient http, Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadOptionalNodeAsync(http, uri, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is JsonException or HttpRequestException)
        {
            return null;
        }
    }

    private static async Task<OscQueryNode> ReadNodeAsync(HttpClient http, Uri uri, CancellationToken cancellationToken)
    {
        return JsonConvert.DeserializeObject<OscQueryNode>(await http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false))
            ?? throw new JsonSerializationException("OSCQuery returned an empty node");
    }

    private static string ReadAvatarId(OscQueryNode change)
    {
        return !string.Equals(change.FullPath, "/avatar/change", StringComparison.Ordinal) || change.Value is not [string id] || string.IsNullOrWhiteSpace(id)
            ? throw new JsonSerializationException("OSCQuery returned an invalid avatar id")
            : id;
    }
}
