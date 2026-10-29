using System.Runtime.InteropServices;

namespace VRCFaceTracking.Core;

public static partial class ProcessPriority
{
    private const uint AboveNormalPriorityClass = 0x8000;

    public static bool TryRaiseToAboveNormal() => SetPriorityClass(GetCurrentProcess(), AboveNormalPriorityClass);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetPriorityClass(IntPtr process, uint priorityClass);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr GetCurrentProcess();
}
