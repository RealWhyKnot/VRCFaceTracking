using System.Text.Json.Serialization;
using VRCFaceTracking.Core.Contracts;

namespace VRCFaceTracking.Core.Models.ParameterDefinition.FileBased;

public sealed class AvatarConfigFile : IAvatarInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public IReadOnlyList<AvatarConfigFileParameter> ConfigParameters { get; set; } = [];

    [JsonIgnore]
    public IReadOnlyList<IParameterDefinition> Parameters => ConfigParameters;

    [JsonIgnore]
    public bool FullFaceTracking { get; set; }
}
