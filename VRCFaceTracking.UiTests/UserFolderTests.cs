namespace VRCFaceTracking.UiTests;

public class UserFolderTests
{
    [Fact]
    public void PersistentDataUsesTheTestDataDirectory() =>
        Assert.Equal(Environment.GetEnvironmentVariable(Core.Utils.DataDirectoryEnvironmentVariable), Core.Utils.PersistentDataDirectory);
}
