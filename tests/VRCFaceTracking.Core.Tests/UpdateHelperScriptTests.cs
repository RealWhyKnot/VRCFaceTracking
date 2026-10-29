using System.Diagnostics;
using System.Text;
using VRCFaceTracking.Core.Updates;

namespace VRCFaceTracking.Core.Tests;

public sealed class UpdateHelperScriptTests
{
    [Fact]
    public void QuotesPathsAndUsesCrlf()
    {
        string script = UpdateHelperScript.Build(4242, @"C:\stage\extracted", @"C:\stage", @"C:\Tom's Apps\VRCFT", @"C:\Tom's Apps\VRCFT\VRCFaceTracking.exe", @"C:\logs\update.log");
        Assert.Contains("Wait-Process -Id 4242", script, StringComparison.Ordinal);
        Assert.Contains(@"'C:\Tom''s Apps\VRCFT'", script, StringComparison.Ordinal);
        Assert.Contains(@"Remove-Item -LiteralPath 'C:\stage' -Recurse -Force -ErrorAction SilentlyContinue", script, StringComparison.Ordinal);
        Assert.True(script.IndexOf("Start-Process", StringComparison.Ordinal) < script.IndexOf("Remove-Item", StringComparison.Ordinal));
        Assert.Contains(@"Start-Process -FilePath 'C:\Tom''s Apps\VRCFT\VRCFaceTracking.exe'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", script.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.All(script, c => Assert.True(c < 128));
        Assert.DoesNotContain("??", script, StringComparison.Ordinal);
        Assert.DoesNotContain("&&", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupScriptRunsSilentSetupThenRelaunches()
    {
        string script = UpdateHelperScript.BuildSetup(4242, @"C:\stage\VRCFaceTracking-Setup-2026.10.7.0.exe", @"C:\stage", @"C:\Tom's Apps\VRCFT", @"C:\Tom's Apps\VRCFT\VRCFaceTracking.exe", @"C:\logs\update.log");
        int setup = script.IndexOf(@"Start-Process -FilePath 'C:\stage\VRCFaceTracking-Setup-2026.10.7.0.exe' -ArgumentList '/S /D=C:\Tom''s Apps\VRCFT' -Wait -PassThru", StringComparison.Ordinal);
        int relaunch = script.IndexOf(@"Start-Process -FilePath 'C:\Tom''s Apps\VRCFT\VRCFaceTracking.exe'", StringComparison.Ordinal);
        Assert.Contains("Wait-Process -Id 4242", script, StringComparison.Ordinal);
        Assert.Contains("Get-Process -Name 'VRCFaceTracking.ModuleProcess'", script, StringComparison.Ordinal);
        Assert.Contains("$setup.ExitCode -ne 0", script, StringComparison.Ordinal);
        Assert.True(setup > 0 && setup < relaunch);
        Assert.True(relaunch < script.IndexOf("Remove-Item", StringComparison.Ordinal));
        Assert.DoesNotContain("\n", script.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.All(script, c => Assert.True(c < 128));
    }

    [Fact]
    public void PowerShellScriptsAreWrittenAsUtf8WithBom()
    {
        string root = Path.Combine(Path.GetTempPath(), "vrcft-bom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string Install = @"C:\Users\Jürgen 山田\AppData\Local\VRCFaceTracking";
            string path = Path.Combine(root, "apply.ps1");
            string[] scripts =
            [
                UpdateHelperScript.Build(4242, @"C:\stage\extracted", @"C:\stage", Install, Install + @"\VRCFaceTracking.exe", @"C:\logs\update.log"),
                UpdateHelperScript.BuildSetup(4242, @"C:\stage\VRCFaceTracking-Setup.exe", @"C:\stage", Install, Install + @"\VRCFaceTracking.exe", @"C:\logs\update.log"),
            ];

            foreach (string? script in scripts)
            {
                UpdateHelperScript.WritePowerShell(path, script);
                byte[] bytes = File.ReadAllBytes(path);
                Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
                string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes, 3, bytes.Length - 3);
                Assert.Equal(script, text);
                Assert.Contains("'" + Install + "'", text, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WindowsPowerShellAppliesAnUpdateIntoANonAsciiPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string top = Path.Combine(Path.GetTempPath(), "vrcft-apply-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(top, "Jürgen 山田");
        string staging = Path.Combine(root, "update");
        string payload = Path.Combine(staging, "extracted");
        string install = Path.Combine(root, "VRCFaceTracking");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(install);
        await File.WriteAllTextAsync(Path.Combine(payload, "payload.txt"), "new");
        await File.WriteAllTextAsync(Path.Combine(install, "payload.txt"), "old");
        try
        {
            int exitedId;
            using (Process exited = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { UseShellExecute = false, CreateNoWindow = true })!)
            {
                await exited.WaitForExitAsync();
                exitedId = exited.Id;
            }

            string scriptPath = Path.Combine(staging, "apply.ps1");
            UpdateHelperScript.WritePowerShell(scriptPath, UpdateHelperScript.Build(exitedId, payload, staging, install, Path.Combine(install, "missing.exe"), Path.Combine(root, "update.log")));

            var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (string? arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath })
            {
                psi.ArgumentList.Add(arg);
            }

            using Process shell = Process.Start(psi)!;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await shell.WaitForExitAsync(cts.Token);
            }
            finally
            {
                if (!shell.HasExited)
                {
                    shell.Kill(entireProcessTree: true);
                }
            }

            Assert.Equal(0, shell.ExitCode);
            Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(install, "payload.txt"), cts.Token));
            Assert.False(Directory.Exists(staging));
        }
        finally
        {
            Directory.Delete(top, recursive: true);
        }
    }

    [Fact]
    public void ShScriptIsLfOnlyAndRelaunches()
    {
        string script = UpdateHelperScript.BuildSh(4242, "/tmp/stage/extracted", "/tmp/stage", "/opt/Tom's Apps/VRCFT", "/opt/Tom's Apps/VRCFT/VRCFaceTracking", "/home/tom/.local/share/VRCFaceTracking/logs/update.log");
        Assert.DoesNotContain('\r', script);
        Assert.Contains("kill -0 \"$pid\"", script, StringComparison.Ordinal);
        Assert.Contains("chmod +x", script, StringComparison.Ordinal);
        Assert.Contains("'/opt/Tom'\\''s Apps/VRCFT/VRCFaceTracking'", script, StringComparison.Ordinal);
        Assert.Contains("nohup \"$exe\"", script, StringComparison.Ordinal);
        Assert.Contains("rm -rf \"$staging\"", script, StringComparison.Ordinal);
        Assert.True(script.IndexOf("nohup", StringComparison.Ordinal) < script.IndexOf("rm -rf", StringComparison.Ordinal));
        Assert.All(script, c => Assert.True(c < 128));
    }
}
