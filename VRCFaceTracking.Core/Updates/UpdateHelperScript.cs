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

    public static string BuildSh(int processId, string payloadRoot, string stagingDir, string installDir, string exePath, string logPath)
    {
        var sb = new StringBuilder();
        void Line(string text) => sb.Append(text).Append('\n');

        Line("#!/bin/sh");
        Line($"pid={processId}");
        Line($"payload={ShQuote(payloadRoot)}");
        Line($"staging={ShQuote(stagingDir)}");
        Line($"install={ShQuote(installDir)}");
        Line($"exe={ShQuote(exePath)}");
        Line($"log={ShQuote(logPath)}");
        Line("i=0");
        Line("while kill -0 \"$pid\" 2>/dev/null; do");
        Line("  i=$((i+1))");
        Line("  if [ \"$i\" -ge 300 ]; then break; fi");
        Line("  sleep 1");
        Line("done");
        Line("applied=0");
        Line("n=0");
        Line("while [ \"$n\" -lt 5 ]; do");
        Line("  if cp -a \"$payload/.\" \"$install/\" 2>>\"$log\"; then applied=1; break; fi");
        Line("  n=$((n+1))");
        Line("  sleep 1");
        Line("done");
        Line("if [ \"$applied\" -ne 1 ]; then");
        Line("  echo 'update apply FAILED; the install may be partially overwritten' >>\"$log\"");
        Line("fi");
        Line("chmod +x \"$install/VRCFaceTracking\" \"$install/VRCFaceTracking.ModuleProcess\" 2>>\"$log\"");
        Line("if [ -x \"$exe\" ]; then");
        Line("  cd \"$install\" && nohup \"$exe\" >/dev/null 2>>\"$log\" &");
        Line("else");
        Line("  echo \"relaunch failed: $exe is not executable\" >>\"$log\"");
        Line("fi");
        Line("rm -rf \"$staging\"");
        return sb.ToString();
    }

    private static string ShQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
