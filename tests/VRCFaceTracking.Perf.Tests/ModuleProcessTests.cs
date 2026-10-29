using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.Perf.Synthetic;
using Xunit.Abstractions;

namespace VRCFaceTracking.Perf.Tests;

public sealed class ModuleProcessTests : IDisposable
{
    private const double StreamingModuleCpuBudgetPercent = 5;
    private const double IdleModuleCpuBudgetPercent = 3;
    private const double StreamingHostCpuBudgetPercent = 12;
    private const double IdleHostCpuBudgetPercent = 2;
    private const long ModuleWorkingSetBudget = 128L * 1024 * 1024;
    private const string TieredCompilationVariable = "DOTNET_TieredCompilation";

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(4);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vrcft-perftests-" + Guid.NewGuid().ToString("N"));

    private readonly ITestOutputHelper _output;

    public ModuleProcessTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed class SyntheticModuleSource : IModuleDataService
    {
        private readonly string _assemblyPath;

        public SyntheticModuleSource(string assemblyPath)
        {
            _assemblyPath = assemblyPath;
        }

        public IEnumerable<InstallableTrackingModule> GetInstalledModules() =>
            [new InstallableTrackingModule { ModuleName = "Synthetic Perf Module", AssemblyLoadPath = _assemblyPath }];

        public IEnumerable<InstallableTrackingModule> GetLegacyModules() => [];
        public Task<IEnumerable<InstallableTrackingModule>> GetRemoteModulesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int?> GetMyRatingAsync(TrackingModuleMetadata moduleMetadata, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetMyRatingAsync(TrackingModuleMetadata moduleMetadata, int rating, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task IncrementDownloadsAsync(TrackingModuleMetadata moduleMetadata, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private string PidFile => Path.Combine(_root, "module.pid");

    private Process? FindModuleProcess()
    {
        if (!File.Exists(PidFile))
        {
            return null;
        }

        try
        {
            var process = Process.GetProcessById(int.Parse(File.ReadAllText(PidFile), CultureInfo.InvariantCulture));
            return process.HasExited ? null : process;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private string ModuleLogTail()
    {
        string directory = Path.Combine(_root, "logs");
        if (!Directory.Exists(directory))
        {
            return "no module log directory";
        }

        var tails = new List<string>();
        foreach (string file in Directory.GetFiles(directory))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            string[] lines = reader.ReadToEnd().Split('\n');
            tails.Add($"{Path.GetFileName(file)}:{Environment.NewLine}{string.Join('\n', lines.TakeLast(30))}");
        }
        return tails.Count == 0 ? "module log directory is empty" : string.Join(Environment.NewLine, tails);
    }

    private sealed record ModuleRun(double ModuleCpuPercent, long ModuleWorkingSet, CpuSample Host, long Merges);

    private async Task<ModuleRun> RunAsync(string mode)
    {
        PrepareEnvironment(mode);

        await using var harness = new SenderHarness(allMutations: true, drainSink: true);
        string modulePath = Path.Combine(AppContext.BaseDirectory, "VRCFaceTracking.Perf.Module.dll");
        using var logs = new CapturingLoggerProvider();
        using var loggerFactory = new LoggerFactory([logs]);
        var manager = new UnifiedLibManager(loggerFactory, new InlineDispatcher(), new SyntheticModuleSource(modulePath), new NullSettings(), new LogLevelGate(), TimeProvider.System);
        try
        {
            await harness.StartAsync();
            int version0 = UnifiedTracking.DataVersion;
            manager.Initialize();
            await WaitForModuleAsync(mode, version0, logs);

            using Process? module = FindModuleProcess();
            Assert.NotNull(module);
            await Task.Delay(Settle);
            return MeasureModule(module, mode);
        }
        finally
        {
            manager.TeardownAllAndReset();
            UnifiedLibManager.ShutdownSandboxServer();
            using Process? leftover = FindModuleProcess();
            leftover?.Kill();
        }
    }

    private void PrepareEnvironment(string mode)
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable(LogDirectories.EnvironmentVariable, Path.Combine(_root, "logs"));
        Environment.SetEnvironmentVariable(Core.Utils.DataDirectoryEnvironmentVariable, Path.Combine(_root, "data"));
        Environment.SetEnvironmentVariable(SyntheticModule.ModeVariable, mode);
        Environment.SetEnvironmentVariable(SyntheticModule.PidFileVariable, PidFile);
        Environment.SetEnvironmentVariable(TieredCompilationVariable, "0");
    }

    private async Task WaitForModuleAsync(string mode, int version0, CapturingLoggerProvider logs)
    {
        long deadline = Stopwatch.GetTimestamp() + (long)(StartTimeout.TotalSeconds * Stopwatch.Frequency);
        while (UnifiedLibManager.ExpressionStatus != ModuleState.Active && Stopwatch.GetTimestamp() < deadline)
        {
            await Task.Delay(50);
        }
        Assert.True(UnifiedLibManager.ExpressionStatus == ModuleState.Active,
            $"module status {UnifiedLibManager.ExpressionStatus} after {StartTimeout.TotalSeconds:N0} s{Environment.NewLine}{logs.Tail()}{Environment.NewLine}{ModuleLogTail()}");
        while (string.Equals(mode, SyntheticModule.StreamMode, StringComparison.Ordinal) && UnifiedTracking.DataVersion == version0 && Stopwatch.GetTimestamp() < deadline)
        {
            await Task.Delay(50);
        }
    }

    private ModuleRun MeasureModule(Process module, string mode)
    {
        int versions0 = UnifiedTracking.DataVersion;
        var self = Process.GetCurrentProcess();
        Dictionary<int, (string Name, double CpuMs)> moduleThreads0 = CpuClock.Threads(module);
        Dictionary<int, (string Name, double CpuMs)> hostThreads0 = CpuClock.Threads(self);
        double moduleCpu0 = CpuClock.CpuMilliseconds(module);
        long start = Stopwatch.GetTimestamp();
        CpuSample host = Measure.Process(Window);
        double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        double moduleCpu = (CpuClock.CpuMilliseconds(module) - moduleCpu0) / elapsedMs * 100;
        Dictionary<int, (string Name, double CpuMs)> hostThreads = CpuClock.Threads(self);
        _output.Report($"module process {mode} threads", CpuClock.TopThreads(moduleThreads0, CpuClock.Threads(module), elapsedMs));
        _output.Report($"host {mode} threads", CpuClock.TopThreads(hostThreads0, hostThreads, elapsedMs));
        module.Refresh();
        var run = new ModuleRun(moduleCpu, module.WorkingSet64, host, UnifiedTracking.DataVersion - versions0);

        _output.Report($"module process {mode}",
            $"module {run.ModuleCpuPercent:N2}% of one core, working set {run.ModuleWorkingSet / 1048576.0:N1} MB | host {host.PercentOfOneCore:N2}%, {host.AllocatedKbPerSecond:N1} KB/s, gc {host.Gen0}/{host.Gen1}/{host.Gen2} | {run.Merges} merges in {Window.TotalSeconds:N0} s");
        return run;
    }

    [Fact]
    public async Task StreamingModuleStaysWithinCpuAndMemoryBudget()
    {
        ModuleRun run = await RunAsync(SyntheticModule.StreamMode);

        Assert.True(run.Merges > SyntheticModule.StreamHz * Window.TotalSeconds * 0.8, $"only {run.Merges} samples merged");
        Assert.True(run.ModuleCpuPercent < StreamingModuleCpuBudgetPercent, $"module process {run.ModuleCpuPercent:N2}% of one core while streaming");
        Assert.True(run.Host.PercentOfOneCore < StreamingHostCpuBudgetPercent, $"host {run.Host.PercentOfOneCore:N2}% of one core while streaming");
        Assert.True(run.ModuleWorkingSet < ModuleWorkingSetBudget, $"module working set {run.ModuleWorkingSet} B");
        Assert.Equal(0, run.Host.Gen2);
    }

    [Fact]
    public async Task IdleModuleUsesAlmostNoCpu()
    {
        ModuleRun run = await RunAsync(SyntheticModule.IdleMode);

        Assert.True(run.ModuleCpuPercent < IdleModuleCpuBudgetPercent, $"module process {run.ModuleCpuPercent:N2}% of one core while idle");
        Assert.True(run.Host.PercentOfOneCore < IdleHostCpuBudgetPercent, $"host {run.Host.PercentOfOneCore:N2}% of one core while idle");
        Assert.True(run.ModuleWorkingSet < ModuleWorkingSetBudget, $"module working set {run.ModuleWorkingSet} B");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(SyntheticModule.ModeVariable, value: null);
        Environment.SetEnvironmentVariable(SyntheticModule.PidFileVariable, value: null);
        Environment.SetEnvironmentVariable(TieredCompilationVariable, value: null);
    }
}
