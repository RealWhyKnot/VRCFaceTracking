using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Library;

namespace VRCFaceTracking.Core.Tests;

public class ModuleInitProgressTests
{
    private sealed class InlineDispatcher : IDispatcherService
    {
        public void Run(Action action) => action();
    }

    private static ModuleInitProgress NewProgress() => new(new InlineDispatcher());

    [Fact]
    public void AllEntriesAddedBeforeFirstResolve_NoneMissed()
    {
        var progress = NewProgress();
        progress.Begin();
        progress.Add("a.dll", "a");
        progress.Add("b.dll", "b");

        progress.Resolve("a.dll");

        Assert.True(progress.IsInitializing);
        Assert.Single(progress.Modules);
        Assert.Equal("b", progress.Modules[0].Name);

        progress.Resolve("b.dll");

        Assert.False(progress.IsInitializing);
        Assert.False(progress.HasPending);
        Assert.Empty(progress.Modules);
    }

    [Fact]
    public void AddAfterLastResolve_IsIgnored()
    {
        var progress = NewProgress();
        progress.Begin();
        progress.Add("a.dll", "a");
        progress.Resolve("a.dll");

        progress.Add("b.dll", "b");

        Assert.Empty(progress.Modules);
    }

    [Fact]
    public void TimeOutPending_KeepsEntriesButClearsPending()
    {
        var progress = NewProgress();
        progress.Begin();
        progress.Add("a.dll", "a");
        progress.Advance("a.dll", ModuleInitStage.Initializing);

        progress.TimeOutPending();

        Assert.True(progress.IsInitializing);
        Assert.False(progress.HasPending);
        Assert.Equal(ModuleInitStage.TimedOut, progress.Modules[0].Stage);
    }

    [Fact]
    public void FinishAfterTimeOutPending_ClearsEverything()
    {
        var progress = NewProgress();
        progress.Begin();
        progress.Add("a.dll", "a");
        progress.TimeOutPending();

        progress.Finish();

        Assert.False(progress.IsInitializing);
        Assert.False(progress.HasPending);
        Assert.Empty(progress.Modules);
    }
}
