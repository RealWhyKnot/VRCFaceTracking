using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using Xunit.Abstractions;

namespace VRCFaceTracking.PerfTests;

public sealed class MutationTests(ITestOutputHelper output)
{
    private const double MutationBudgetUs = 50;

    public static TheoryData<string> Mutations()
    {
        var names = new TheoryData<string>();
        foreach (var mutation in TrackingMutation.GetImplementingMutations())
        {
            names.Add(mutation.GetType().Name);
        }
        return names;
    }

    private static TrackingMutation Create(string typeName)
    {
        var mutation = TrackingMutation.GetImplementingMutations().Single(m => m.GetType().Name == typeName);
        mutation.Logger = NullLogger.Instance;
        mutation.IsActive = true;
        mutation.Initialize(new UnifiedTrackingData());
        return mutation;
    }

    private static UnifiedTrackingData[] Frames()
    {
        var frames = new UnifiedTrackingData[100];
        for (var f = 0; f < frames.Length; f++)
        {
            var data = new UnifiedTrackingData();
            for (var i = 0; i < data.Shapes.Length; i++)
            {
                data.Shapes[i].Weight = (f * 7 + i * 13) % 100 / 100f;
            }
            data.Eye.Left.Openness = f % 50 / 50f;
            data.Eye.Right.Openness = f % 40 / 40f;
            data.Eye.Left.Gaze.x = f % 20 / 20f - 0.5f;
            data.Eye.Right.Gaze.x = f % 20 / 20f - 0.5f;
            frames[f] = data;
        }
        return frames;
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public void Mutation_AllocatesNothingPerTick(string mutationType)
    {
        var mutation = Create(mutationType);
        var frames = Frames();
        var work = new UnifiedTrackingData();
        var frame = 0;
        void Tick()
        {
            work.CopyPropertiesOf(frames[frame++ % frames.Length]);
            mutation.MutateData(ref work);
        }

        var bytes = Measure.AllocatedBytes(Tick, 10_000);
        var us = Measure.BestMicrosecondsPerOp(Tick, 1000);

        output.Report($"mutation {mutationType}", $"{bytes} B over 10000 ticks, {us:N2} us per tick, budget {MutationBudgetUs} us");
        Assert.Equal(0, bytes);
        Assert.True(us < MutationBudgetUs, $"{us:N2} us per tick");
    }
}
