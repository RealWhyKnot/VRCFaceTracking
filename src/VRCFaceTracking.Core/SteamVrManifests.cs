using System.Runtime.InteropServices;
using System.Text.Json;

namespace VRCFaceTracking.Core;

public static partial class SteamVrManifests
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
        foreach (string configDir in ReadStrings(openVrPathsFile, "config"))
        {
            foreach (string path in ReadStrings(Path.Combine(configDir, "appconfig.json"), "manifest_paths"))
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
        JsonElement? root = ReadJson(manifestPath);
        if (root is not { } r || !r.TryGetProperty("applications", out JsonElement apps) || apps.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement app in apps.EnumerateArray())
        {
            if (app.ValueKind == JsonValueKind.Object && app.TryGetProperty("app_key", out JsonElement key) && key.ValueKind == JsonValueKind.String)
            {
                return key.GetString();
            }
        }

        return null;
    }

    public static SteamVrManifestPlan Plan(string manifest, string appKey, IEnumerable<string> registered, Func<string, string?> appKeyOf)
    {
        var rivals = new List<string>();
        bool ours = false;
        foreach (string path in registered)
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
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
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
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!OperatingSystem.IsWindows() || !full.Contains('~', StringComparison.Ordinal))
        {
            return full;
        }

        Span<char> buffer = stackalloc char[1024];
        uint length = GetLongPathName(full, buffer, (uint)buffer.Length);
        return length > 0 && length < buffer.Length ? new string(buffer[..(int)length]) : full;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetLongPathNameW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetLongPathName(string shortPath, Span<char> longPath, uint size);

    private static IEnumerable<string> ReadStrings(string file, string property)
    {
        return ReadJson(file) is not { } root || !root.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array
            ? Array.Empty<string>()
            : array.EnumerateArray()
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
