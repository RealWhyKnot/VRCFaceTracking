using Newtonsoft.Json;

namespace VRCFaceTracking.Core.Osc.Query;

public class OscQueryNode
{
    public OscQueryNode()
    {
    }

    public OscQueryNode(string fullPath, AccessValues access = AccessValues.NoValue, string? oscType = null)
    {
        FullPath = fullPath;
        Access = access;
        OscType = oscType;
    }

    [JsonProperty("DESCRIPTION")]
    public string? Description { get; set; }

    [JsonProperty("FULL_PATH")]
    public string FullPath { get; set; } = string.Empty;

    [JsonProperty("ACCESS")]
    public AccessValues Access { get; set; }

    [JsonProperty("CONTENTS")]
    public IReadOnlyDictionary<string, OscQueryNode>? Contents { get; set; }

    [JsonProperty("TYPE")]
    public string? OscType { get; set; }

    [JsonProperty("VALUE")]
    public IReadOnlyList<object>? Value { get; set; }

    [JsonIgnore]
    public string ParentPath
    {
        get
        {
            int length = Math.Max(1, FullPath.LastIndexOf('/'));
            return FullPath[..length];
        }

    }

    [JsonIgnore]
    public string Name => FullPath[(FullPath.LastIndexOf('/') + 1)..];

    public override string ToString()
    {
        string result = JsonConvert.SerializeObject(this, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
        });
        return result;
    }
}
