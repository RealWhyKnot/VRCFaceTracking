using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace VRCFaceTracking.Core;

public enum BuildChannel
{
    Dev,
    Beta,
    Release
}

public static class BuildInfo
{
    public const string ChannelMetadataKey = "VftBuildChannel";

    public static readonly BuildChannel Channel = ParseChannel(typeof(BuildInfo).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == ChannelMetadataKey)?.Value);

    public static readonly Version Version = typeof(BuildInfo).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    public static string VersionString => $"{Version.Major}.{Version.Minor}.{Version.Build}.{Version.Revision}";

    public static string ChannelName => Channel.ToString().ToLowerInvariant();

    public static bool VerboseForced => Channel != BuildChannel.Release;

    public static BuildChannel ParseChannel(string? value) =>
        Enum.TryParse<BuildChannel>(value, true, out var channel) ? channel : BuildChannel.Dev;

    public static string HeaderBlock(string processName)
    {
        var sb = new StringBuilder();
        sb.AppendLine(new string('=', 80));
        sb.AppendLine($"VRCFaceTracking {VersionString} ({ChannelName}) - {processName}");
        sb.AppendLine($"Started:  {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine($"Machine:  {Environment.MachineName}");
        sb.AppendLine($"OS:       {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($"Runtime:  {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Process:  {Environment.ProcessPath} (PID {Environment.ProcessId})");
        sb.AppendLine($"Command:  {Environment.CommandLine}");
        sb.AppendLine(new string('=', 80));
        return sb.ToString();
    }
}
