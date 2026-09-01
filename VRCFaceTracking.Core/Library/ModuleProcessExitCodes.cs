namespace VRCFaceTracking.Core.Library;

public static class ModuleProcessExitCodes
{
    public const int OK = 0;
    public const int INVALID_ARGS = 101;
    public const int NETWORK_CONNECTION_TIMED_OUT = 102;
    public const int EXCEPTION_CRASH = 103;
    public const int MODULE_LOAD_FAILED = 104;
    public const int PARENT_EXITED = 105;

    public static string Describe(int code) => code switch
    {
        OK => "clean exit",
        INVALID_ARGS => "invalid arguments",
        NETWORK_CONNECTION_TIMED_OUT => "connection to host timed out",
        EXCEPTION_CRASH => "unhandled exception",
        MODULE_LOAD_FAILED => "module assembly failed to load",
        PARENT_EXITED => "host process exited",
        -1 or 1 => $"killed (exit code {code})",
        unchecked((int)0xC000013A) => "terminated by console close",
        unchecked((int)0xC0000005) => "access violation (0xC0000005)",
        unchecked((int)0xE0434352) => "unhandled .NET exception (0xE0434352)",
        > 0 and < 256 => $"exit code {code}",
        _ => $"native exit code 0x{(uint)code:X8}"
    };
}
