using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace VRCFaceTracking.PerfTests;

internal readonly record struct CpuSample(double ElapsedMs, double CpuMs, long AllocatedBytes, int Gen0, int Gen1, int Gen2)
{
    public double PercentOfOneCore => ElapsedMs <= 0 ? 0 : CpuMs / ElapsedMs * 100;
    public double AllocatedKbPerSecond => ElapsedMs <= 0 ? 0 : AllocatedBytes / 1024.0 / (ElapsedMs / 1000);
}

internal static class CpuClock
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryProcessCycleTime(IntPtr process, out ulong cycles);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    private static readonly Lazy<double> CyclesPerMs = new(Calibrate);

    private static double Calibrate()
    {
        var best = 0.0;
        for (var trial = 0; trial < 5; trial++)
        {
            QueryThreadCycleTime(GetCurrentThread(), out var c0);
            var start = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < 20)
            {
            }
            QueryThreadCycleTime(GetCurrentThread(), out var c1);
            best = Math.Max(best, (c1 - c0) / Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        return best;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(int access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int threadId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetThreadDescription(IntPtr thread, out IntPtr description);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private const int ThreadQueryLimitedInformation = 0x0800;

    public static Dictionary<int, (string Name, double CpuMs)> Threads(Process process)
    {
        var threads = new Dictionary<int, (string, double)>();
        if (!OperatingSystem.IsWindows())
        {
            return threads;
        }

        process.Refresh();
        foreach (ProcessThread thread in process.Threads)
        {
            var handle = OpenThread(ThreadQueryLimitedInformation, false, thread.Id);
            if (handle == IntPtr.Zero)
            {
                continue;
            }
            try
            {
                var name = "";
                if (GetThreadDescription(handle, out var description) >= 0 && description != IntPtr.Zero)
                {
                    name = Marshal.PtrToStringUni(description) ?? "";
                    LocalFree(description);
                }
                if (QueryThreadCycleTime(handle, out var cycles))
                {
                    threads[thread.Id] = (name, cycles / CyclesPerMs.Value);
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }
        return threads;
    }

    public static string TopThreads(Dictionary<int, (string Name, double CpuMs)> before, Dictionary<int, (string Name, double CpuMs)> after, double elapsedMs, int count = 6)
    {
        var deltas = after
            .Select(pair => (pair.Value.Name, Percent: (pair.Value.CpuMs - (before.TryGetValue(pair.Key, out var b) ? b.CpuMs : 0)) / elapsedMs * 100))
            .OrderByDescending(t => t.Percent)
            .Take(count)
            .Select(t => $"{(t.Name.Length == 0 ? "?" : t.Name)} {t.Percent:N2}%");
        return string.Join(", ", deltas);
    }

    public static double CpuMilliseconds(Process process)
    {
        if (OperatingSystem.IsWindows() && QueryProcessCycleTime(process.Handle, out var cycles))
        {
            return cycles / CyclesPerMs.Value;
        }

        process.Refresh();
        return process.TotalProcessorTime.TotalMilliseconds;
    }
}

internal static class Measure
{
    public static long AllocatedBytes(Action op, int iterations, int warmup = 1000)
    {
        for (var i = 0; i < warmup; i++)
        {
            op();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            op();
        }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    public static double BestMicrosecondsPerOp(Action op, int opsPerRound, int rounds = 7)
    {
        for (var i = 0; i < opsPerRound; i++)
        {
            op();
        }

        var best = double.MaxValue;
        for (var round = 0; round < rounds; round++)
        {
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < opsPerRound; i++)
            {
                op();
            }
            var us = Stopwatch.GetElapsedTime(start).TotalMicroseconds / opsPerRound;
            best = Math.Min(best, us);
        }
        return best;
    }

    public static CpuSample Process(TimeSpan window, Process? process = null)
    {
        var target = process ?? System.Diagnostics.Process.GetCurrentProcess();
        var cpu0 = CpuClock.CpuMilliseconds(target);
        var alloc0 = GC.GetTotalAllocatedBytes(precise: true);
        var (g0, g1, g2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var start = Stopwatch.GetTimestamp();

        Thread.Sleep(window);

        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var cpu = CpuClock.CpuMilliseconds(target) - cpu0;
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - alloc0;
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
