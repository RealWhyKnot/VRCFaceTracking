namespace VRCFaceTracking.Core.Library;

public static class ModuleProcessExitCodes
{
    public const int OK = 0;
    public const int InvalidArgs = 101;
    public const int NetworkConnectionTimedOut = 102;
    public const int ExceptionCrash = 103;
    public const int ModuleLoadFailed = 104;
    public const int ParentExited = 105;

    public static string Describe(int code) => code switch
    {
        OK => "clean exit",
        InvalidArgs => "invalid arguments",
        NetworkConnectionTimedOut => "connection to host timed out",
        ExceptionCrash => "unhandled exception",
        ModuleLoadFailed => "module assembly failed to load",
        ParentExited => "host process exited",
        -1 or 1 => $"killed (exit code {code})",
        unchecked((int)0xC000013A) => "terminated by console close",
        unchecked(0x40010004) => "terminated externally (0x40010004)",
        unchecked((int)0xC0000005) => "access violation (0xC0000005)",
        unchecked((int)0xE0434352) => "unhandled .NET exception (0xE0434352)",
        > 0 and < 256 => $"exit code {code}",
        _ => $"native exit code 0x{(uint)code:X8}",
    };
}
