using Xunit.Abstractions;

namespace VRCFaceTracking.Perf.Tests;

public sealed class TickPathTests
{
    private const double DataTickBudgetUs = 400;
    private const double UnchangedTickBudgetUs = 200;
    private const int SmoothingSettleTicks = 3000;

    private int _frame;

    private readonly ITestOutputHelper _output;

    public TickPathTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private void DataTick(SenderHarness harness)
    {
        Tracking.WriteFrame(_frame++);
        harness.Tick();
    }

    [Fact]
    public async Task DataTickEveryParameterAndMutationAllocatesNothing()
    {
        await using var harness = new SenderHarness(allMutations: true);
        long bytes = Measure.AllocatedBytes(() => DataTick(harness), 5000);

        _output.Report("data tick allocation", $"{bytes} B over 5000 ticks");
        Assert.Equal(0, bytes);
    }

    [Fact]
    public async Task DataTickEveryParameterAndMutationStaysWithinCpuBudget()
    {
        await using var harness = new SenderHarness(allMutations: true);
        Tracking.WriteFrame(_frame++);
        int messages = harness.Tick();
        double us = Measure.BestMicrosecondsPerOp(() => DataTick(harness), 500);

        _output.Report("data tick", $"{us:N1} us for ~{messages} OSC messages, budget {DataTickBudgetUs} us");
        Assert.True(messages > 100, $"only {messages} messages per changed tick; the fixture lost its relevant parameters");
        Assert.True(us < DataTickBudgetUs, $"{us:N1} us per tick");
    }

    [Fact]
    public async Task UnchangedTickSendsNothingAndStaysCheap()
    {
        await using var harness = new SenderHarness(allMutations: true);
        Tracking.WriteFrame(_frame);
        for (int i = 0; i < SmoothingSettleTicks; i++)
        {
            harness.Tick();
        }

        int sent = harness.Tick();
        double us = Measure.BestMicrosecondsPerOp(() => harness.Tick(), 500);
        long bytes = Measure.AllocatedBytes(() => harness.Tick(), 5000);

        _output.Report("unchanged tick", $"{us:N1} us, {bytes} B over 5000 ticks, budget {UnchangedTickBudgetUs} us");
        Assert.Equal(0, sent);
        Assert.Equal(0, bytes);
        Assert.True(us < UnchangedTickBudgetUs, $"{us:N1} us per unchanged tick");
    }
}
