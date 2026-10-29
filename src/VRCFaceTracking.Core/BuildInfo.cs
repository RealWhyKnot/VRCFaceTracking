using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace VRCFaceTracking.Core;

public static class BuildInfo
{
    public const string ChannelMetadataKey = "VftBuildChannel";

    public static readonly BuildChannel Channel = ParseChannel(typeof(BuildInfo).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => string.Equals(a.Key, ChannelMetadataKey, StringComparison.Ordinal))?.Value);

    public static readonly Version Version = typeof(BuildInfo).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    public static readonly string VersionString = typeof(BuildInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? $"{Version.Major}.{Version.Minor}.{Version.Build}.{Version.Revision}";

    public static string ChannelName => Channel switch
    {
        BuildChannel.Dev => "dev",
        BuildChannel.Beta => "beta",
        BuildChannel.Release => "release",
        _ => Channel.ToString(),
    };

    public static bool VerboseForced => Channel != BuildChannel.Release;

    public static BuildChannel ParseChannel(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out BuildChannel channel) ? channel : BuildChannel.Dev;

    public static string HeaderBlock(string processName, DateTimeOffset started)
    {
        var sb = new StringBuilder();
        sb.AppendLine(new string('=', 80));
        sb.AppendLine(CultureInfo.InvariantCulture, $"VRCFaceTracking {VersionString} ({ChannelName}) - {processName}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Started:  {started.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Machine:  {Environment.MachineName}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"OS:       {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Runtime:  {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Process:  {Environment.ProcessPath} (PID {Environment.ProcessId})");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Command:  {Environment.CommandLine}");
        sb.AppendLine(new string('=', 80));
        return sb.ToString();
    }
}
