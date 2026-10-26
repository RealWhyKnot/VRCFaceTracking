using Newtonsoft.Json;

namespace VRCFaceTracking.Core.OSC.Query;

internal static class OscQueryAvatarReader
{
    internal static async Task<OscQueryNode> ReadAsync(HttpClient http, Uri avatarEndpoint)
    {
        try
        {
            return await ReadNodeAsync(http, avatarEndpoint);
        }
        catch (JsonException)
        {
        }

        var avatarBase = new Uri(avatarEndpoint.AbsoluteUri.TrimEnd('/') + "/");
        var changeEndpoint = new Uri(avatarBase, "change");
        var parametersEndpoint = new Uri(avatarBase, "parameters");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var change = await ReadNodeAsync(http, changeEndpoint);
            var idBefore = ReadAvatarId(change);
            var parameters = await ReadNodeAsync(http, parametersEndpoint);
            if (parameters.FullPath != "/avatar/parameters" || parameters.Contents == null)
            {
                throw new JsonSerializationException("OSCQuery returned an invalid avatar parameter tree");
            }

            if (idBefore != ReadAvatarId(await ReadNodeAsync(http, changeEndpoint)))
            {
                continue;
            }

            return new OscQueryNode("/avatar")
            {
                Contents = new Dictionary<string, OscQueryNode>
                {
                    ["change"] = change,
                    ["parameters"] = parameters,
                },
            };
        }

        throw new JsonSerializationException("Avatar changed while reading its OSCQuery data");
    }

    private static async Task<OscQueryNode> ReadNodeAsync(HttpClient http, Uri uri)
    {
        return JsonConvert.DeserializeObject<OscQueryNode>(await http.GetStringAsync(uri))
            ?? throw new JsonSerializationException("OSCQuery returned an empty node");
    }

    private static string ReadAvatarId(OscQueryNode change)
    {
        if (change.FullPath != "/avatar/change" || change.Value is not [string id] || string.IsNullOrWhiteSpace(id))
        {
            throw new JsonSerializationException("OSCQuery returned an invalid avatar id");
        }

        return id;
    }
}
