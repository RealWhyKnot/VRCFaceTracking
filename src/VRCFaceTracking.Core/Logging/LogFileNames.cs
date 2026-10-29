namespace VRCFaceTracking.Core.Logging;

public static class LogFileNames
{
    public const string MainPattern = "vrcft_????-??-??_??-??-??*.log";
    public const string ModulePattern = "vrcft_module_*.log";

    public static string Main(DateTime timestamp) => $"vrcft_{timestamp:yyyy-MM-dd_HH-mm-ss}.log";

    public static string Module(string moduleName, DateTime timestamp) =>
        $"vrcft_module_{Sanitize(moduleName)}_{timestamp:yyyy-MM-dd_HH-mm-ss}.log";

    public static string Sanitize(string name)
    {
        const string invalid = "<>:\"/\\|?*";
        var chars = name.Select(c => invalid.Contains(c) || char.IsControl(c) || c == ' ' ? '_' : c).ToArray();
        var result = new string(chars).Trim('_');
        return result.Length == 0 ? "module" : result;
    }
}
