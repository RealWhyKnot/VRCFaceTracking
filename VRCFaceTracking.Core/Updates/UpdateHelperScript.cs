using System.Text;

namespace VRCFaceTracking.Core.Updates;

public static class UpdateHelperScript
{
    public static string Build(int processId, string payloadRoot, string stagingDir, string installDir, string exePath, string logPath)
    {
        var sb = new StringBuilder();
        void Line(string text) => sb.Append(text).Append("\r\n");

        Line("$ErrorActionPreference = 'Stop'");
        Line("try {");
        Line($"    Wait-Process -Id {processId} -Timeout 300 -ErrorAction SilentlyContinue");
        Line("    $attempt = 0");
        Line("    while ($true) {");
        Line("        try {");
        Line($"            Copy-Item -Path (Join-Path {Quote(payloadRoot)} '*') -Destination {Quote(installDir)} -Recurse -Force");
        Line("            break");
        Line("        } catch {");
        Line("            $attempt++");
        Line("            if ($attempt -ge 10) { throw }");
        Line("            Start-Sleep -Seconds 1");
        Line("        }");
        Line("    }");
        Line($"    Start-Process -FilePath {Quote(exePath)} -WorkingDirectory {Quote(installDir)}");
        Line($"    Remove-Item -LiteralPath {Quote(stagingDir)} -Recurse -Force -ErrorAction SilentlyContinue");
        Line("} catch {");
        Line($"    $_ | Out-String | Add-Content -LiteralPath {Quote(logPath)}");
        Line("    exit 1");
        Line("}");
        return sb.ToString();
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
