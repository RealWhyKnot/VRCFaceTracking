namespace VRCFaceTracking.Core.Library;

public static class ModuleProcessExitCodes
{
    public const int OK = 0;
    public const int INVALID_ARGS = -1;
    public const int NETWORK_CONNECTION_TIMED_OUT = -2;
    public const int EXCEPTION_CRASH = -3;
    public const int MODULE_LOAD_FAILED = -4;
    public const int PARENT_EXITED = -5;

    public static string Describe(int code) => code switch
    {
        OK => "clean exit",
        INVALID_ARGS => "invalid arguments",
        NETWORK_CONNECTION_TIMED_OUT => "connection to host timed out",
        EXCEPTION_CRASH => "unhandled exception",
        MODULE_LOAD_FAILED => "module assembly failed to load",
        PARENT_EXITED => "host process exited",
        > -100 and < 0 => $"unknown exit code {code}",
        _ => $"native exit code 0x{(uint)code:X8}"
    };
}
