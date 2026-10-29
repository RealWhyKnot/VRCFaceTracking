using System.Runtime.InteropServices;
using System.Text.Json;

namespace VRCFaceTracking.Core.Tests;

public sealed partial class SteamVrManifestsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Manifest(string folder, string appKey)
    {
        string dir = Directory.CreateDirectory(Path.Combine(_root, folder)).FullName;
        string path = Path.Combine(dir, "app.vrmanifest");
        File.WriteAllText(path, "{\n  // SteamVR tolerates comments\n  \"source\": \"builtin\",\n  \"applications\": [ { \"app_key\": \"" + appKey + "\", \"launch_type\": \"binary\" }, ]\n}");
        return path;
    }

    private string OpenVrPaths(params string[][] configs)
    {
        var configDirs = new List<string>();
        for (int i = 0; i < configs.Length; i++)
        {
            string dir = Directory.CreateDirectory(Path.Combine(_root, $"config{i}")).FullName;
            File.WriteAllText(Path.Combine(dir, "appconfig.json"), JsonSerializer.Serialize(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["manifest_paths"] = configs[i] }));
            configDirs.Add(dir);
        }

        string file = Path.Combine(_root, "openvrpaths.vrpath");
        File.WriteAllText(file, JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal) { ["config"] = configDirs, ["version"] = 1 }));
        return file;
    }

    [Fact]
    public void ReadsAppKeyFromLenientManifest()
    {
        Assert.Equal("benaclejames.vrcft", SteamVrManifests.AppKey(Manifest("a", "benaclejames.vrcft")));
        Assert.Null(SteamVrManifests.AppKey(Path.Combine(_root, "missing.vrmanifest")));
        string broken = Path.Combine(_root, "broken.vrmanifest");
        File.WriteAllText(broken, "{ not json");
        Assert.Null(SteamVrManifests.AppKey(broken));
    }

    [Fact]
    public void RegisteredMergesConfigDirsWithoutDuplicates()
    {
        string a = Manifest("a", "x");
        string b = Manifest("b", "y");
        string file = OpenVrPaths([a, b], [a.ToUpperInvariant()]);
        IReadOnlyList<string> registered = SteamVrManifests.Registered(file);
        Assert.Equal(OperatingSystem.IsWindows() ? 2 : 3, registered.Count);
        Assert.Equal(a, registered[0]);
        Assert.Equal(b, registered[1]);
    }

    [Fact]
    public void RegisteredIsEmptyWithoutSteamVr()
    {
        Assert.Empty(SteamVrManifests.Registered(Path.Combine(_root, "missing.vrpath")));
        string file = Path.Combine(_root, "empty.vrpath");
        File.WriteAllText(file, "{}");
        Assert.Empty(SteamVrManifests.Registered(file));
    }

    [Fact]
    public void PlanFindsOtherCopiesWithTheSameKey()
    {
        string ours = Manifest("ours", "benaclejames.vrcft");
        string oldCopy = Manifest("old", "benaclejames.vrcft");
        string other = Manifest("other", "someone.else");
        string gone = Path.Combine(_root, "deleted", "app.vrmanifest");

        SteamVrManifestPlan plan = SteamVrManifests.Plan(ours, "benaclejames.vrcft", [oldCopy, other, gone], SteamVrManifests.AppKey);
        Assert.False(plan.Registered);
        Assert.Equal([oldCopy], plan.Rivals);

        plan = SteamVrManifests.Plan(ours, "benaclejames.vrcft", [other, ours + Path.DirectorySeparatorChar], SteamVrManifests.AppKey);
        Assert.True(plan.Registered);
        Assert.Empty(plan.Rivals);
    }

    [Fact]
    public void SamePathNormalisesSeparatorsAndCase()
    {
        string path = Path.Combine(_root, "dir", "app.vrmanifest");
        Assert.True(SteamVrManifests.SamePath(path, Path.Combine(_root, "dir", ".", "app.vrmanifest")));
        Assert.Equal(OperatingSystem.IsWindows(), SteamVrManifests.SamePath(path, path.ToUpperInvariant()));
        Assert.False(SteamVrManifests.SamePath(path, Path.Combine(_root, "other", "app.vrmanifest")));
    }

    [Fact]
    public void SamePathExpandsShortNames()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string manifest = Manifest("a folder with a long name", "x");
        Span<char> buffer = stackalloc char[1024];
        uint length = GetShortPathName(manifest, buffer, (uint)buffer.Length);
        Assert.True(length > 0 && length < buffer.Length);
        string shortPath = new(buffer[..(int)length]);
        if (string.Equals(shortPath, manifest, StringComparison.Ordinal))
        {
            return;
        }

        Assert.True(SteamVrManifests.SamePath(manifest, shortPath));
        Assert.True(SteamVrManifests.SamePath(shortPath, manifest));
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetShortPathNameW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetShortPathName(string longPath, Span<char> shortPath, uint size);
}
