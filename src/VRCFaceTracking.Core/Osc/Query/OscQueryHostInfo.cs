using Newtonsoft.Json;

namespace VRCFaceTracking.Core.Osc.Query;

public sealed class OscQueryHostInfo
{
    [JsonProperty("NAME")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("EXTENSIONS")]
    public IReadOnlyDictionary<string, bool> Extensions { get; set; } = new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        { "ACCESS", true },
        { "CLIPMODE", false },
        { "RANGE", true },
        { "TYPE", true },
        { "VALUE", true },
    };

    [JsonProperty("OSC_IP")]
    public string? OscIP { get; set; }

    [JsonProperty("OSC_PORT")]
    public int OscPort { get; set; } = 6969;

    [JsonProperty("OSC_TRANSPORT")]
    public string OscTransport { get; set; } = "UDP";

    public OscQueryHostInfo()
    {

    }

    public override string ToString()
    {
        string result = JsonConvert.SerializeObject(this);
        return result;
    }
}
