using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using Xunit.Abstractions;

namespace VRCFaceTracking.Perf.Tests;

public sealed class MutationTests
{
    private const double MutationBudgetUs = 50;

    private readonly ITestOutputHelper _output;

    public MutationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static TheoryData<string> Mutations()
    {
        var names = new TheoryData<string>();
        foreach (TrackingMutation mutation in TrackingMutation.GetImplementingMutations())
        {
            names.Add(mutation.GetType().Name);
        }
        return names;
    }

    private static TrackingMutation Create(string typeName)
    {
        TrackingMutation mutation = TrackingMutation.GetImplementingMutations().Single(m => string.Equals(m.GetType().Name, typeName, StringComparison.Ordinal));
        mutation.Logger = NullLogger.Instance;
        mutation.IsActive = true;
        mutation.Initialize(new UnifiedTrackingData());
        return mutation;
    }

    private static UnifiedTrackingData[] Frames()
    {
        var frames = new UnifiedTrackingData[100];
        for (int f = 0; f < frames.Length; f++)
        {
            var data = new UnifiedTrackingData();
            for (int i = 0; i < data.Shapes.Length; i++)
            {
                data.Shapes[i].Weight = ((f * 7) + (i * 13)) % 100 / 100f;
            }
            data.Eye.Left.Openness = f % 50 / 50f;
            data.Eye.Right.Openness = f % 40 / 40f;
            data.Eye.Left.Gaze.x = (f % 20 / 20f) - 0.5f;
            data.Eye.Right.Gaze.x = (f % 20 / 20f) - 0.5f;
            frames[f] = data;
        }
        return frames;
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public void MutationAllocatesNothingPerTick(string mutationType)
    {
        TrackingMutation mutation = Create(mutationType);
        UnifiedTrackingData[] frames = Frames();
        var work = new UnifiedTrackingData();
        int frame = 0;
        void Tick()
        {
            work.CopyPropertiesOf(frames[frame++ % frames.Length]);
            mutation.MutateData(ref work);
        }

        long bytes = Measure.AllocatedBytes(Tick, 10_000);
        double us = Measure.BestMicrosecondsPerOp(Tick, 1000);

        _output.Report($"mutation {mutationType}", $"{bytes} B over 10000 ticks, {us:N2} us per tick, budget {MutationBudgetUs} us");
        Assert.Equal(0, bytes);
        Assert.True(us < MutationBudgetUs, $"{us:N2} us per tick");
    }
}
