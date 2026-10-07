using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace VRCFaceTracking.Core;

public sealed record SteamVrManifestPlan(IReadOnlyList<string> Rivals, bool Registered);

public static class SteamVrManifests
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static string OpenVrPathsFile => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "openvr", "openvrpaths.vrpath")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "openvr", "openvrpaths.vrpath");

    public static IReadOnlyList<string> Registered(string openVrPathsFile)
    {
        var result = new List<string>();
        foreach (var configDir in ReadStrings(openVrPathsFile, "config"))
        {
            foreach (var path in ReadStrings(Path.Combine(configDir, "appconfig.json"), "manifest_paths"))
            {
                if (!result.Any(p => SamePath(p, path)))
                {
                    result.Add(path);
                }
            }
        }

        return result;
    }

    public static string? AppKey(string manifestPath)
    {
        var root = ReadJson(manifestPath);
        if (root is not { } r || !r.TryGetProperty("applications", out var apps) || apps.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var app in apps.EnumerateArray())
        {
            if (app.ValueKind == JsonValueKind.Object && app.TryGetProperty("app_key", out var key) && key.ValueKind == JsonValueKind.String)
            {
                return key.GetString();
            }
        }

        return null;
    }

    public static SteamVrManifestPlan Plan(string manifest, string appKey, IEnumerable<string> registered, Func<string, string?> appKeyOf)
    {
        var rivals = new List<string>();
        var ours = false;
        foreach (var path in registered)
        {
            if (SamePath(path, manifest))
            {
                ours = true;
            }
            else if (string.Equals(appKeyOf(path), appKey, StringComparison.Ordinal))
            {
                rivals.Add(path);
            }
        }

        return new SteamVrManifestPlan(rivals, ours);
    }

    public static bool SamePath(string a, string b)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        try
        {
            return string.Equals(LongPath(a), LongPath(b), comparison);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(a, b, comparison);
        }
    }

    private static string LongPath(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!OperatingSystem.IsWindows() || !full.Contains('~'))
        {
            return full;
        }

        var buffer = new StringBuilder(1024);
        var length = GetLongPathName(full, buffer, (uint)buffer.Capacity);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : full;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint size);

    private static IEnumerable<string> ReadStrings(string file, string property)
    {
        if (ReadJson(file) is not { } root || !root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return array.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => s.Length > 0)
            .ToList();
    }

    private static JsonElement? ReadJson(string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file), JsonOptions);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
