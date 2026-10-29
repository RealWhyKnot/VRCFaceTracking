using System.Runtime.InteropServices;

namespace VRCFaceTracking.Core;

public static partial class PowerThrottling
{
    private const int ProcessPowerThrottling = 4;
    private const uint CurrentVersion = 1;
    private const uint ExecutionSpeed = 0x1;
    private const uint IgnoreTimerResolution = 0x4;

    public static bool TryOptOut(out uint mask, out int error)
    {
        error = 0;
        int size = Marshal.SizeOf<ThrottlingState>();
        foreach (uint candidate in (ReadOnlySpan<uint>)[ExecutionSpeed | IgnoreTimerResolution, ExecutionSpeed])
        {
            ThrottlingState state = new() { Version = CurrentVersion, ControlMask = candidate, StateMask = 0 };
            if (SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, size))
            {
                mask = candidate;
                return true;
            }

            error = Marshal.GetLastPInvokeError();
        }

        mask = 0;
        return false;
    }

    public static string Describe()
    {
        ThrottlingState state = new() { Version = CurrentVersion };
        return GetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, Marshal.SizeOf<ThrottlingState>())
            ? FormattableString.Invariant($"control=0x{state.ControlMask:X} state=0x{state.StateMask:X}")
            : FormattableString.Invariant($"read-back unavailable (error {Marshal.GetLastPInvokeError()})");
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessInformation(IntPtr process, int informationClass, ref ThrottlingState state, int size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessInformation(IntPtr process, int informationClass, ref ThrottlingState state, int size);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr GetCurrentProcess();

    [StructLayout(LayoutKind.Sequential)]
    private struct ThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }
}
