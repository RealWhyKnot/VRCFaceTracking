using System.Text.Json.Serialization;
using VRCFaceTracking.Core.Contracts;

namespace VRCFaceTracking.Core.Models.ParameterDefinition.FileBased;

public sealed class AvatarConfigFileParameter : IParameterDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("input")]
    public AvatarConfigFileIODefinition? Input { get; set; }

    [JsonPropertyName("output")]
    public AvatarConfigFileIODefinition? Output { get; set; }

    [JsonIgnore]
    public string Address => RequiredInput.Address;

    [JsonIgnore]
    public Type Type => RequiredInput.Type;

    private AvatarConfigFileIODefinition RequiredInput => Input ?? throw new InvalidOperationException($"Parameter {Name} has no input");
}
