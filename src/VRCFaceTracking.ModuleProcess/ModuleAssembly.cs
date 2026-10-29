using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.ModuleProcess;

internal sealed partial class ModuleAssembly
{
    public string ModulePath { get; set; }
    public bool Loaded { get; set; }
    private readonly ILogger<ModuleProcessMain> _logger;
    private readonly ILoggerFactory _loggerFactory;
    public ExtTrackingModule? TrackingModule { get; private set; }
    public CancellationTokenSource? UpdateCts { get; set; }

    public ModuleAssembly(ILogger<ModuleProcessMain> logger, ILoggerFactory loggerFactory, string dllPath)
    {
        if (!File.Exists(dllPath))
        {
            throw new ArgumentException($"Invalid file path: \"{dllPath}\" does not exist!", nameof(dllPath));
        }
        if (!string.Equals(Path.GetExtension(dllPath), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{dllPath} is not a DLL file and cannot be loaded.", nameof(dllPath));
        }

        _logger = logger;
        _loggerFactory = loggerFactory;
        ModulePath = dllPath;
        Loaded = false;
    }

    public void LoadAssembly()
    {
        if (Loaded)
        {
            return;
        }

        try
        {
            var alc = new AssemblyLoadContext(ModulePath, isCollectible: true);
            Assembly assembly = alc.LoadFromAssemblyPath(ModulePath);

            AssemblyName[] references = assembly.GetReferencedAssemblies();
            bool oldRefs = false;
            foreach (AssemblyName reference in references)
            {
                if (reference.Name is "VRCFaceTracking" or "VRCFaceTracking.Core")
                {
                    if (reference.Version < new Version(5, 0, 0, 0))
                    {
                        LogModuleReferencesOlderVersionVRCFaceTracking(_logger, Path.GetFileName(ModulePath));
                        oldRefs = true;
                    }
                }
            }
            if (oldRefs)
            {
                return;
            }

            foreach (Type type in assembly.GetExportedTypes())
            {
                if (!type.IsSubclassOf(typeof(ExtTrackingModule)))
                {
                    continue;
                }

                LogProperlyImplementsExtTrackingModule(_logger, type.Name);
                Loaded = true;
                TrackingModule = CreateModule(type);
                break;
            }
        }
        catch (Exception e) when (e is IOException or BadImageFormatException or ReflectionTypeLoadException or TypeLoadException)
        {
            LogAssemblyNotAbleLoadedSkipping(_logger, e.Message);
        }
    }

    private ExtTrackingModule? CreateModule(Type type)
    {
        LogLoadingExternalModule(_logger, type.Assembly.FullName);

        try
        {
            var module = (ExtTrackingModule)Activator.CreateInstance(type)!;
            module.Logger = _loggerFactory.CreateLogger(type.Name);
            return module;
        }
        catch (Exception e) when (e is MemberAccessException or TargetInvocationException)
        {
            LogExceptionLoadingModule(_logger, e, type.Assembly.FullName);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Module {Dll} references an older version of VRCFaceTracking. Skipping.")]
    private static partial void LogModuleReferencesOlderVersionVRCFaceTracking(ILogger logger, string dll);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Module} properly implements ExtTrackingModule.")]
    private static partial void LogProperlyImplementsExtTrackingModule(ILogger logger, string module);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Error} Assembly not able to be loaded. Skipping.")]
    private static partial void LogAssemblyNotAbleLoadedSkipping(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loading External Module {Assembly}")]
    private static partial void LogLoadingExternalModule(ILogger logger, string? assembly);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exception loading {Dll}. Skipping.")]
    private static partial void LogExceptionLoadingModule(ILogger logger, Exception exception, string? dll);
}
