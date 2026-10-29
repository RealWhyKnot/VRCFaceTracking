using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VRCFaceTracking.Perf.Tests;

internal static partial class CpuClock
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryProcessCycleTime(IntPtr process, out ulong cycles);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr GetCurrentThread();

    private static readonly Lazy<double> CyclesPerMs = new(Calibrate);

    private static double Calibrate()
    {
        double best = 0.0;
        for (int trial = 0; trial < 5; trial++)
        {
            QueryThreadCycleTime(GetCurrentThread(), out ulong c0);
            long start = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < 20)
            {
            }
            QueryThreadCycleTime(GetCurrentThread(), out ulong c1);
            best = Math.Max(best, (c1 - c0) / Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        return best;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr OpenThread(int access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int threadId);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetThreadDescription(IntPtr thread, out IntPtr description);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr LocalFree(IntPtr memory);

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
            nint handle = OpenThread(ThreadQueryLimitedInformation, inherit: false, thread.Id);
            if (handle == IntPtr.Zero)
            {
                continue;
            }
            try
            {
                string name = "";
                if (GetThreadDescription(handle, out nint description) >= 0 && description != IntPtr.Zero)
                {
                    name = Marshal.PtrToStringUni(description) ?? "";
                    LocalFree(description);
                }
                if (QueryThreadCycleTime(handle, out ulong cycles))
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
        IEnumerable<string> deltas = after
            .Select(pair => (pair.Value.Name, Percent: (pair.Value.CpuMs - (before.TryGetValue(pair.Key, out (string Name, double CpuMs) b) ? b.CpuMs : 0)) / elapsedMs * 100))
            .OrderByDescending(t => t.Percent)
            .Take(count)
            .Select(t => $"{(t.Name.Length == 0 ? "?" : t.Name)} {t.Percent:N2}%");
        return string.Join(", ", deltas);
    }

    public static double CpuMilliseconds(Process process)
    {
        if (OperatingSystem.IsWindows() && QueryProcessCycleTime(process.Handle, out ulong cycles))
        {
            return cycles / CyclesPerMs.Value;
        }

        process.Refresh();
        return process.TotalProcessorTime.TotalMilliseconds;
    }
}
