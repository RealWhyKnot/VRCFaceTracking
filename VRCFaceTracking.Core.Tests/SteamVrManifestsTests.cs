using System.Text.Json;

namespace VRCFaceTracking.Core.Tests;

public class SteamVrManifestsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private string Manifest(string folder, string appKey)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, folder)).FullName;
        var path = Path.Combine(dir, "app.vrmanifest");
        File.WriteAllText(path, "{\n  // SteamVR tolerates comments\n  \"source\": \"builtin\",\n  \"applications\": [ { \"app_key\": \"" + appKey + "\", \"launch_type\": \"binary\" }, ]\n}");
        return path;
    }

    private string OpenVrPaths(params string[][] configs)
    {
        var configDirs = new List<string>();
        for (var i = 0; i < configs.Length; i++)
        {
            var dir = Directory.CreateDirectory(Path.Combine(_root, $"config{i}")).FullName;
            File.WriteAllText(Path.Combine(dir, "appconfig.json"), JsonSerializer.Serialize(new Dictionary<string, string[]> { ["manifest_paths"] = configs[i] }));
            configDirs.Add(dir);
        }

        var file = Path.Combine(_root, "openvrpaths.vrpath");
        File.WriteAllText(file, JsonSerializer.Serialize(new Dictionary<string, object> { ["config"] = configDirs, ["version"] = 1 }));
        return file;
    }

    [Fact]
    public void ReadsAppKeyFromLenientManifest()
    {
        Assert.Equal("benaclejames.vrcft", SteamVrManifests.AppKey(Manifest("a", "benaclejames.vrcft")));
        Assert.Null(SteamVrManifests.AppKey(Path.Combine(_root, "missing.vrmanifest")));
        var broken = Path.Combine(_root, "broken.vrmanifest");
        File.WriteAllText(broken, "{ not json");
        Assert.Null(SteamVrManifests.AppKey(broken));
    }

    [Fact]
    public void RegisteredMergesConfigDirsWithoutDuplicates()
    {
        var a = Manifest("a", "x");
        var b = Manifest("b", "y");
        var file = OpenVrPaths(new[] { a, b }, new[] { a.ToUpperInvariant() });
        var registered = SteamVrManifests.Registered(file);
        Assert.Equal(OperatingSystem.IsWindows() ? 2 : 3, registered.Count);
        Assert.Equal(a, registered[0]);
        Assert.Equal(b, registered[1]);
    }

    [Fact]
    public void RegisteredIsEmptyWithoutSteamVr()
    {
        Assert.Empty(SteamVrManifests.Registered(Path.Combine(_root, "missing.vrpath")));
        var file = Path.Combine(_root, "empty.vrpath");
        File.WriteAllText(file, "{}");
        Assert.Empty(SteamVrManifests.Registered(file));
    }

    [Fact]
    public void PlanFindsOtherCopiesWithTheSameKey()
    {
        var ours = Manifest("ours", "benaclejames.vrcft");
        var oldCopy = Manifest("old", "benaclejames.vrcft");
        var other = Manifest("other", "someone.else");
        var gone = Path.Combine(_root, "deleted", "app.vrmanifest");

        var plan = SteamVrManifests.Plan(ours, "benaclejames.vrcft", new[] { oldCopy, other, gone }, SteamVrManifests.AppKey);
        Assert.False(plan.Registered);
        Assert.Equal(new[] { oldCopy }, plan.Rivals);

        plan = SteamVrManifests.Plan(ours, "benaclejames.vrcft", new[] { other, ours + Path.DirectorySeparatorChar }, SteamVrManifests.AppKey);
        Assert.True(plan.Registered);
        Assert.Empty(plan.Rivals);
    }

    [Fact]
    public void SamePathNormalisesSeparatorsAndCase()
    {
        var path = Path.Combine(_root, "dir", "app.vrmanifest");
        Assert.True(SteamVrManifests.SamePath(path, Path.Combine(_root, "dir", ".", "app.vrmanifest")));
        Assert.Equal(OperatingSystem.IsWindows(), SteamVrManifests.SamePath(path, path.ToUpperInvariant()));
        Assert.False(SteamVrManifests.SamePath(path, Path.Combine(_root, "other", "app.vrmanifest")));
    }
}
