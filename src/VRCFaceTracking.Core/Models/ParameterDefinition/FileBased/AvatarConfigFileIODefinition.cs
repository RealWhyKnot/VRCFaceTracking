using System.Text.Json.Serialization;
using VRCFaceTracking.Core.Osc;

namespace VRCFaceTracking.Core.Models.ParameterDefinition.FileBased;

public sealed class AvatarConfigFileIODefinition
{
    [JsonPropertyName("address")]
    public string Address { get; set => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("type")]
    public string TypeName { get; set => field = value ?? string.Empty; } = string.Empty;

    [JsonIgnore]
    public Type Type => OscUtils.TypeConversions.Where(conversion => string.Equals(conversion.Value.configType, TypeName, StringComparison.Ordinal)).Select(conversion => conversion.Key).FirstOrDefault().Item1;
}
