using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace VRCFaceTracking.Core;

public static class VRChat
{
    private const string VRChatSteamAppId = "438100";

    public static string VRCOSCDirectory
    {
        get; private set;
    } = string.Empty;

    public static void EnsureVRCOSCDirectory()
    {
        VRCOSCDirectory = OperatingSystem.IsWindows()
            ? Path.Combine($"{Environment.GetEnvironmentVariable("localappdata")}Low", "VRChat", "VRChat", "OSC")
            : OperatingSystem.IsMacOS()
                ? string.Empty
                : FindProtonOscDirectory() ?? string.Empty;
    }

    private static string? FindProtonOscDirectory()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] possiblePaths =
        [
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
        ];
        string? steamPath = Array.Find(possiblePaths, Directory.Exists);
        if (string.IsNullOrEmpty(steamPath))
        {
            return null;
        }

        string steamLibrariesPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(steamLibrariesPath))
        {
            return null;
        }

        string? libraryPath = FindVRChatLibrary(ParseVdfFile(File.ReadAllText(steamLibrariesPath)));
        return libraryPath == null
            ? null
            : Path.Combine(libraryPath, "steamapps", "compatdata", VRChatSteamAppId, "pfx", "drive_c",
                "users", "steamuser", "AppData", "LocalLow", "VRChat", "VRChat", "OSC");
    }

    private static string? FindVRChatLibrary(Dictionary<string, object> libraryFolders)
    {
        if (!libraryFolders.TryGetValue("libraryfolders", out object? librariesObj)
            || librariesObj is not Dictionary<string, object> libraries)
        {
            return null;
        }

        foreach (object library in libraries.Values)
        {
            if (library is Dictionary<string, object> libraryData
                && libraryData.TryGetValue("path", out object? pathObj)
                && libraryData.TryGetValue("apps", out object? appsObj)
                && appsObj is Dictionary<string, object> apps
                && apps.ContainsKey(VRChatSteamAppId))
            {
                return pathObj as string;
            }
        }

        return null;
    }

    private static Dictionary<string, object> ParseVdfFile(string vdfContent)
    {
        var parser = new VdfParser(vdfContent);
        return parser.Parse();
    }

    private sealed class VdfParser
    {
        private readonly string _content;
        private int _position;

        public VdfParser(string content)
        {
            _content = content;
            _position = 0;
        }

        public Dictionary<string, object> Parse()
        {
            SkipWhitespace();
            return ParseObject();
        }

        private Dictionary<string, object> ParseObject()
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);

            while (_position < _content.Length)
            {
                SkipWhitespace();

                if (_position < _content.Length && _content[_position] == '}')
                {
                    _position++;
                    break;
                }

                string key = ParseString();
                if (string.IsNullOrEmpty(key))
                {
                    break;
                }

                SkipWhitespace();

                object value;

                if (_position < _content.Length && _content[_position] == '{')
                {
                    _position++;
                    value = ParseObject();
                }
                else
                {
                    value = ParseString();
                }

                result[key] = value;
            }

            return result;
        }

        private string ParseString()
        {
            SkipWhitespace();

            if (_position < _content.Length && _content[_position] == '"')
            {
                _position++;

                int startPos = _position;

                while (_position < _content.Length && _content[_position] != '"')
                {
                    if (_content[_position] == '\\' && _position + 1 < _content.Length)
                    {
                        _position += 2;
                    }
                    else
                    {
                        _position++;
                    }
                }

                if (_position < _content.Length)
                {
                    string result = _content[startPos.._position];
                    _position++;
                    return result;
                }
            }

            return string.Empty;
        }

        private void SkipWhitespace()
        {
            while (_position < _content.Length)
            {
                char c = _content[_position];

                if (char.IsWhiteSpace(c) || c == '\r' || c == '\n' || c == '\t')
                {
                    _position++;
                }
                else if (c == '/' && _position + 1 < _content.Length && _content[_position + 1] == '/')
                {
                    _position += 2;
                    while (_position < _content.Length && _content[_position] != '\n')
                    {
                        _position++;
                    }
                }
                else
                {
                    break;
                }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    public static bool ForceEnableOsc()
    {
        using RegistryKey? regKey = Registry.CurrentUser.OpenSubKey("Software\\VRChat\\VRChat", writable: true);
        if (regKey == null)
        {
            return true;
        }

        IEnumerable<string> keys = regKey.GetValueNames().Where(x => x.StartsWith("VRC_INPUT_OSC", StringComparison.Ordinal) || x.StartsWith("UI.Settings.Osc", StringComparison.Ordinal));

        bool wasOscForced = false;
        foreach (string key in keys)
        {
            if (regKey.GetValue(key) is 0)
            {
                regKey.SetValue(key, 1);
                wasOscForced = true;
            }
        }

        return wasOscForced;
    }

    public static bool IsVrChatRunning() => Process.GetProcesses().Any(x => x.ProcessName is "VRChat.exe" or "VRChat");
}
