using System.Diagnostics;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace VRCFaceTracking.Perf.Tests;

internal static class Measure
{
    public static long AllocatedBytes(Action op, int iterations, int warmup = 1000)
    {
        for (int i = 0; i < warmup; i++)
        {
            op();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
        {
            op();
        }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    public static double BestMicrosecondsPerOp(Action op, int opsPerRound, int rounds = 7)
    {
        for (int i = 0; i < opsPerRound; i++)
        {
            op();
        }

        double best = double.MaxValue;
        for (int round = 0; round < rounds; round++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < opsPerRound; i++)
            {
                op();
            }
            double us = Stopwatch.GetElapsedTime(start).TotalMicroseconds / opsPerRound;
            best = Math.Min(best, us);
        }
        return best;
    }

    public static CpuSample Process(TimeSpan window, Process? process = null)
    {
        Process target = process ?? System.Diagnostics.Process.GetCurrentProcess();
        double cpu0 = CpuClock.CpuMilliseconds(target);
        long alloc0 = GC.GetTotalAllocatedBytes(precise: true);
        (int g0, int g1, int g2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        long start = Stopwatch.GetTimestamp();

        Thread.Sleep(window);

        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        double cpu = CpuClock.CpuMilliseconds(target) - cpu0;
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - alloc0;
        return new CpuSample(elapsed, cpu, allocated,
            GC.CollectionCount(0) - g0, GC.CollectionCount(1) - g1, GC.CollectionCount(2) - g2);
    }

    public static long LiveHeapBytes()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    public static void Report(this ITestOutputHelper output, string name, string value) =>
        output.WriteLine($"perf {name}: {value}");
}
