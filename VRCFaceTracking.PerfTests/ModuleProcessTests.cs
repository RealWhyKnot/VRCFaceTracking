using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.PerfTests.Module;
using Xunit.Abstractions;

namespace VRCFaceTracking.PerfTests;

public sealed class ModuleProcessTests(ITestOutputHelper output) : IDisposable
{
    private const double StreamingModuleCpuBudgetPercent = 3;
    private const double IdleModuleCpuBudgetPercent = 1;
    private const double StreamingHostCpuBudgetPercent = 12;
    private const double IdleHostCpuBudgetPercent = 2;
    private const long ModuleWorkingSetBudget = 128L * 1024 * 1024;
    private const string TieredCompilationVariable = "DOTNET_TieredCompilation";

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(4);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vrcft-perftests-" + Guid.NewGuid().ToString("N"));

    private sealed class SyntheticModuleSource(string assemblyPath) : IModuleDataService
    {
        public IEnumerable<InstallableTrackingModule> GetInstalledModules() =>
            new[] { new InstallableTrackingModule { ModuleName = "Synthetic Perf Module", AssemblyLoadPath = assemblyPath } };

        public IEnumerable<InstallableTrackingModule> GetLegacyModules() => Array.Empty<InstallableTrackingModule>();
        public Task<IEnumerable<InstallableTrackingModule>> GetRemoteModules() => throw new NotSupportedException();
        public Task<int?> GetMyRatingAsync(TrackingModuleMetadata moduleMetadata) => throw new NotSupportedException();
        public Task SetMyRatingAsync(TrackingModuleMetadata moduleMetadata, int rating) => throw new NotSupportedException();
        public Task IncrementDownloadsAsync(TrackingModuleMetadata moduleMetadata) => throw new NotSupportedException();
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
            var process = Process.GetProcessById(int.Parse(File.ReadAllText(PidFile), System.Globalization.CultureInfo.InvariantCulture));
            return process.HasExited ? null : process;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private sealed record ModuleRun(double ModuleCpuPercent, long ModuleWorkingSet, CpuSample Host, long Merges);

    private async Task<ModuleRun> Run(string mode)
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable(Core.Utils.LogDirectoryEnvironmentVariable, Path.Combine(_root, "logs"));
        Environment.SetEnvironmentVariable(Core.Utils.DataDirectoryEnvironmentVariable, Path.Combine(_root, "data"));
        Environment.SetEnvironmentVariable(SyntheticModule.ModeVariable, mode);
        Environment.SetEnvironmentVariable(SyntheticModule.PidFileVariable, PidFile);
        Environment.SetEnvironmentVariable(TieredCompilationVariable, "0");

        await using var harness = new SenderHarness(allMutations: true, drainSink: true);
        var modulePath = Path.Combine(AppContext.BaseDirectory, "VRCFaceTracking.PerfTests.Module.dll");
        var manager = new UnifiedLibManager(NullLoggerFactory.Instance, new InlineDispatcher(), new SyntheticModuleSource(modulePath), new NullSettings(), new LogLevelGate());
        try
        {
            await harness.StartAsync();
            var version0 = UnifiedTracking.DataVersion;
            manager.Initialize();

            var deadline = Stopwatch.GetTimestamp() + (long)(StartTimeout.TotalSeconds * Stopwatch.Frequency);
            while (UnifiedLibManager.ExpressionStatus != ModuleState.Active && Stopwatch.GetTimestamp() < deadline)
            {
                await Task.Delay(50);
            }
            Assert.Equal(ModuleState.Active, UnifiedLibManager.ExpressionStatus);
            while (mode == SyntheticModule.StreamMode && UnifiedTracking.DataVersion == version0 && Stopwatch.GetTimestamp() < deadline)
            {
                await Task.Delay(50);
            }

            using var module = FindModuleProcess();
            Assert.NotNull(module);
            Thread.Sleep(Settle);

            var versions0 = UnifiedTracking.DataVersion;
            var self = Process.GetCurrentProcess();
            var moduleThreads0 = CpuClock.Threads(module);
            var hostThreads0 = CpuClock.Threads(self);
            var moduleCpu0 = CpuClock.CpuMilliseconds(module);
            var start = Stopwatch.GetTimestamp();
            var host = Measure.Process(Window);
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var moduleCpu = (CpuClock.CpuMilliseconds(module) - moduleCpu0) / elapsedMs * 100;
            var hostThreads = CpuClock.Threads(self);
            output.Report($"module process {mode} threads", CpuClock.TopThreads(moduleThreads0, CpuClock.Threads(module), elapsedMs));
            output.Report($"host {mode} threads", CpuClock.TopThreads(hostThreads0, hostThreads, elapsedMs));
            module.Refresh();
            var run = new ModuleRun(moduleCpu, module.WorkingSet64, host, UnifiedTracking.DataVersion - versions0);

            output.Report($"module process {mode}",
                $"module {run.ModuleCpuPercent:N2}% of one core, working set {run.ModuleWorkingSet / 1048576.0:N1} MB | host {host.PercentOfOneCore:N2}%, {host.AllocatedKbPerSecond:N1} KB/s, gc {host.Gen0}/{host.Gen1}/{host.Gen2} | {run.Merges} merges in {Window.TotalSeconds:N0} s");
            return run;
        }
        finally
        {
            manager.TeardownAllAndReset();
            UnifiedLibManager.ShutdownSandboxServer();
            using var leftover = FindModuleProcess();
            leftover?.Kill();
        }
    }

    [Fact]
    public async Task StreamingModule_StaysWithinCpuAndMemoryBudget()
    {
        var run = await Run(SyntheticModule.StreamMode);

        Assert.True(run.Merges > SyntheticModule.StreamHz * Window.TotalSeconds * 0.8, $"only {run.Merges} samples merged");
        Assert.True(run.ModuleCpuPercent < StreamingModuleCpuBudgetPercent, $"module process {run.ModuleCpuPercent:N2}% of one core while streaming");
        Assert.True(run.Host.PercentOfOneCore < StreamingHostCpuBudgetPercent, $"host {run.Host.PercentOfOneCore:N2}% of one core while streaming");
        Assert.True(run.ModuleWorkingSet < ModuleWorkingSetBudget, $"module working set {run.ModuleWorkingSet} B");
        Assert.Equal(0, run.Host.Gen2);
    }

    [Fact]
    public async Task IdleModule_UsesAlmostNoCpu()
    {
        var run = await Run(SyntheticModule.IdleMode);

        Assert.True(run.ModuleCpuPercent < IdleModuleCpuBudgetPercent, $"module process {run.ModuleCpuPercent:N2}% of one core while idle");
        Assert.True(run.Host.PercentOfOneCore < IdleHostCpuBudgetPercent, $"host {run.Host.PercentOfOneCore:N2}% of one core while idle");
        Assert.True(run.ModuleWorkingSet < ModuleWorkingSetBudget, $"module working set {run.ModuleWorkingSet} B");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(SyntheticModule.ModeVariable, null);
        Environment.SetEnvironmentVariable(SyntheticModule.PidFileVariable, null);
        Environment.SetEnvironmentVariable(TieredCompilationVariable, null);
    }
}
