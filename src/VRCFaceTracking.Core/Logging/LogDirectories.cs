using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRCFaceTracking.Core.Logging;

public static class LogDirectories
{
    public const string SettingKey = "LogDirectory";
    public const string EnvironmentVariable = "VRCFT_LOG_DIRECTORY";

    public static readonly string Default = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "VRCFaceTracking", "logs");

    public static readonly string Current = Resolve();

    private static string Resolve()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        try
        {
            string? custom = ReadCustom();
            if (string.IsNullOrWhiteSpace(custom))
            {
                return Default;
            }

            Directory.CreateDirectory(custom);
            return custom;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return Default;
        }
    }

    private static string? ReadCustom()
    {
        string settingsPath = Path.Combine(Utils.PersistentDataDirectory, "VRCFaceTracking", "ApplicationData", "LocalSettings.json");
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        string? raw = (string?)JObject.Parse(File.ReadAllText(settingsPath))[SettingKey];
        return string.IsNullOrWhiteSpace(raw) ? null : JsonConvert.DeserializeObject<string>(raw);
    }
}
