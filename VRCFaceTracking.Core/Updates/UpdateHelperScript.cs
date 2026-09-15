using System.Text;

namespace VRCFaceTracking.Core.Updates;

public static class UpdateHelperScript
{
    public static string Build(int processId, string payloadRoot, string stagingDir, string installDir, string exePath, string logPath)
    {
        var sb = new StringBuilder();
        void Line(string text) => sb.Append(text).Append("\r\n");

        Line("$ErrorActionPreference = 'Stop'");
        Line("$applied = $false");
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
        Line("    $applied = $true");
        Line("} catch {");
        Line($"    'update apply FAILED; the install may be partially overwritten' | Add-Content -LiteralPath {Quote(logPath)}");
        Line($"    $_ | Out-String | Add-Content -LiteralPath {Quote(logPath)}");
        Line("}");
        Line("try {");
        Line($"    Start-Process -FilePath {Quote(exePath)} -WorkingDirectory {Quote(installDir)}");
        Line("} catch {");
        Line($"    $_ | Out-String | Add-Content -LiteralPath {Quote(logPath)}");
        Line("}");
        Line($"Remove-Item -LiteralPath {Quote(stagingDir)} -Recurse -Force -ErrorAction SilentlyContinue");
        Line("if (-not $applied) { exit 1 }");
        return sb.ToString();
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
