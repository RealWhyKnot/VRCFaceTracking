using Xunit.Abstractions;

namespace VRCFaceTracking.PerfTests;

public sealed class TickPathTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const double DataTickBudgetUs = 400;
    private const double UnchangedTickBudgetUs = 200;
    private const int SmoothingSettleTicks = 3000;

    private SenderHarness _harness = null!;
    private int _frame;

    public Task InitializeAsync()
    {
        _harness = new SenderHarness(allMutations: true);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    private void DataTick()
    {
        Tracking.WriteFrame(_frame++);
        _harness.Tick();
    }

    [Fact]
    public void DataTick_EveryParameterAndMutation_AllocatesNothing()
    {
        var bytes = Measure.AllocatedBytes(DataTick, 5000);

        output.Report("data tick allocation", $"{bytes} B over 5000 ticks");
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void DataTick_EveryParameterAndMutation_StaysWithinCpuBudget()
    {
        Tracking.WriteFrame(_frame++);
        var messages = _harness.Tick();
        var us = Measure.BestMicrosecondsPerOp(DataTick, 500);

        output.Report("data tick", $"{us:N1} us for ~{messages} OSC messages, budget {DataTickBudgetUs} us");
        Assert.True(messages > 100, $"only {messages} messages per changed tick; the fixture lost its relevant parameters");
        Assert.True(us < DataTickBudgetUs, $"{us:N1} us per tick");
    }

    [Fact]
    public void UnchangedTick_SendsNothingAndStaysCheap()
    {
        Tracking.WriteFrame(_frame);
        for (var i = 0; i < SmoothingSettleTicks; i++)
        {
            _harness.Tick();
        }

        var sent = _harness.Tick();
        var us = Measure.BestMicrosecondsPerOp(() => _harness.Tick(), 500);
        var bytes = Measure.AllocatedBytes(() => _harness.Tick(), 5000);

        output.Report("unchanged tick", $"{us:N1} us, {bytes} B over 5000 ticks, budget {UnchangedTickBudgetUs} us");
        Assert.Equal(0, sent);
        Assert.Equal(0, bytes);
        Assert.True(us < UnchangedTickBudgetUs, $"{us:N1} us per unchanged tick");
    }
}
