using System.Text.Json;
using VRCFaceTracking.Core.Models.ParameterDefinition.FileBased;

namespace VRCFaceTracking.Core.Tests;

public sealed class AvatarConfigFileTests
{
    [Fact]
    public void NullFieldsBecomeEmpty()
    {
        AvatarConfigFile? config = JsonSerializer.Deserialize<AvatarConfigFile>("{\"id\":null,\"name\":null,\"parameters\":null}");

        Assert.NotNull(config);
        Assert.Equal(string.Empty, config.Id);
        Assert.Equal(string.Empty, config.Name);
        Assert.Empty(config.Parameters);
    }

    [Fact]
    public void NullParameterEntriesAndFieldsAreTolerated()
    {
        const string Json = "{\"id\":\"avtr_x\",\"name\":\"X\",\"parameters\":["
            + "null,"
            + "{\"name\":null,\"input\":null},"
            + "{\"name\":\"A\",\"input\":{\"address\":null,\"type\":null}},"
            + "{\"name\":\"B\",\"input\":{\"address\":\"/avatar/parameters/B\",\"type\":\"Float\"}},"
            + "{\"name\":\"C\",\"input\":{\"address\":\"/avatar/parameters/C\",\"type\":\"Vector3\"}}]}";

        AvatarConfigFile? config = JsonSerializer.Deserialize<AvatarConfigFile>(Json);

        Assert.NotNull(config);
        Assert.Equal(4, config.ConfigParameters.Count);
        Assert.Equal(string.Empty, config.ConfigParameters[0].Name);
        AvatarConfigFileParameter[] withInput = [.. config.ConfigParameters.Where(param => param.Input != null)];
        Assert.Equal(["", "/avatar/parameters/B", "/avatar/parameters/C"], withInput.Select(param => param.Address), StringComparer.Ordinal);
        Assert.Equal(typeof(float), withInput[1].Type);
        Assert.Null(withInput[2].Type);
    }
}
