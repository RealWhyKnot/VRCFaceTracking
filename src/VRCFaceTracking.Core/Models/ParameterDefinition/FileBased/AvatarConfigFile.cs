using System.Text.Json.Serialization;
using VRCFaceTracking.Core.Contracts;

namespace VRCFaceTracking.Core.Models.ParameterDefinition.FileBased;

public sealed class AvatarConfigFile : IAvatarInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("parameters")]
    public IReadOnlyList<AvatarConfigFileParameter> ConfigParameters { get; set => field = value is null ? [] : [.. value.OfType<AvatarConfigFileParameter>()]; } = [];

    [JsonIgnore]
    public IReadOnlyList<IParameterDefinition> Parameters => ConfigParameters;

    [JsonIgnore]
    public bool FullFaceTracking { get; set; }
}
