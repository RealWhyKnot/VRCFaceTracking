using System.IO.Compression;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Tests;

public sealed class ModuleInstallerTests : IDisposable
{
    private static readonly Guid ModuleId = Guid.Parse("0b7c5b6e-3f0a-4c55-9d6e-2a9a4f1d7c11");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vrcft-modules-" + Guid.NewGuid().ToString("N"));
    private readonly string _libs;
    private readonly string _installed;
    private readonly HttpListener _listener = new();
    private readonly Dictionary<string, byte[]> _files = new();
    private readonly Task _server;
    private readonly int _port;

    public ModuleInstallerTests()
    {
        _libs = Path.Combine(_root, "CustomLibs");
        _installed = Path.Combine(_libs, ModuleId.ToString());
        Directory.CreateDirectory(_installed);
        File.WriteAllText(Path.Combine(_installed, "Old.dll"), "old");
        File.WriteAllText(Path.Combine(_installed, "module.json"), JsonConvert.SerializeObject(new TrackingModuleMetadata { ModuleId = ModuleId, Version = "1.0", DllFileName = "Old.dll" }));

        _port = Utils.GetRandomFreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();
        _server = Serve();
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
        Directory.Delete(_root, true);
    }

    private async Task Serve()
    {
        try
        {
            while (_listener.IsListening)
            {
                var context = await _listener.GetContextAsync();
                if (_files.TryGetValue(context.Request.Url!.AbsolutePath, out var bytes))
                {
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                }
                else
                {
                    context.Response.StatusCode = 404;
                }

                context.Response.Close();
            }
        }
        catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private TrackingModuleMetadata Remote(string file, string? dllFileName) => new()
    {
        ModuleId = ModuleId,
        Version = "2.0",
        ModuleName = "Test Module",
        DownloadUrl = $"http://127.0.0.1:{_port}/{file}",
        DllFileName = dllFileName!,
    };

    private static byte[] Zip(params (string name, string content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private void AssertOldModuleIntact()
    {
        Assert.Equal("old", File.ReadAllText(Path.Combine(_installed, "Old.dll")));
        var metadata = JsonConvert.DeserializeObject<TrackingModuleMetadata>(File.ReadAllText(Path.Combine(_installed, "module.json")))!;
        Assert.Equal("1.0", metadata.Version);
        Assert.Equal("Old.dll", metadata.DllFileName);
    }

    private void AssertOnlyTheModuleIsLeft()
    {
        Assert.Equal(new[] { _installed }, Directory.GetFileSystemEntries(_libs));
    }

    [Theory]
    [InlineData("Module.zip", null)]
    [InlineData("Module.dll", "Module.dll")]
    public async Task FailedDownloadKeepsTheInstalledModule(string file, string? dllFileName)
    {
        var installer = new ModuleInstaller(NullLogger<ModuleInstaller>.Instance, _libs);

        Assert.Null(await installer.InstallRemoteModule(Remote(file, dllFileName)));

        AssertOldModuleIntact();
        AssertOnlyTheModuleIsLeft();
    }

    [Fact]
    public async Task ZipWithoutADllKeepsTheInstalledModule()
    {
        _files["/Module.zip"] = Zip(("readme.txt", "no dll here"));
        var installer = new ModuleInstaller(NullLogger<ModuleInstaller>.Instance, _libs);

        Assert.Null(await installer.InstallRemoteModule(Remote("Module.zip", null)));

        AssertOldModuleIntact();
        AssertOnlyTheModuleIsLeft();
    }

    [Fact]
    public async Task HashMismatchKeepsTheInstalledModule()
    {
        _files["/Module.dll"] = "new"u8.ToArray();
        var installer = new ModuleInstaller(NullLogger<ModuleInstaller>.Instance, _libs);
        var remote = Remote("Module.dll", "Module.dll");
        remote.FileHash = "00000000000000000000000000000000";

        Assert.Null(await installer.InstallRemoteModule(remote));

        AssertOldModuleIntact();
        AssertOnlyTheModuleIsLeft();
    }

    [Theory]
    [InlineData("Module.zip", null)]
    [InlineData("Module.dll", "Module.dll")]
    public async Task SuccessfulUpdateReplacesTheInstalledModule(string file, string? dllFileName)
    {
        _files["/Module.zip"] = Zip(("Module.dll", "new"), ("data/model.bin", "weights"));
        _files["/Module.dll"] = "new"u8.ToArray();
        var installer = new ModuleInstaller(NullLogger<ModuleInstaller>.Instance, _libs);

        var path = await installer.InstallRemoteModule(Remote(file, dllFileName));

        Assert.Equal(Path.Combine(_installed, "Module.dll"), path);
        Assert.Equal("new", File.ReadAllText(path!));
        Assert.False(File.Exists(Path.Combine(_installed, "Old.dll")));
        var metadata = JsonConvert.DeserializeObject<TrackingModuleMetadata>(File.ReadAllText(Path.Combine(_installed, "module.json")))!;
        Assert.Equal("2.0", metadata.Version);
        Assert.Equal("Module.dll", metadata.DllFileName);
        if (file.EndsWith(".zip"))
        {
            Assert.Equal("weights", File.ReadAllText(Path.Combine(_installed, "data", "model.bin")));
        }

        AssertOnlyTheModuleIsLeft();
    }

    [Fact]
    public async Task FreshInstallCreatesTheModuleDirectory()
    {
        Directory.Delete(_installed, true);
        _files["/Module.dll"] = "new"u8.ToArray();
        var installer = new ModuleInstaller(NullLogger<ModuleInstaller>.Instance, _libs);

        var path = await installer.InstallRemoteModule(Remote("Module.dll", "Module.dll"));

        Assert.Equal(Path.Combine(_installed, "Module.dll"), path);
        Assert.Equal("new", File.ReadAllText(path!));
        AssertOnlyTheModuleIsLeft();
    }
}
