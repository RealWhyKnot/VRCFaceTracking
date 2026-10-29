using Valve.VR;
using VRCFaceTracking.Core;

namespace VRCFaceTracking;

internal static class SteamVrCommand
{
    private const int Ok = 0;
    private const int BadManifest = 1;
    private const int Unreachable = 2;
    private const int NotInstalled = 3;
    private const int Failed = 4;

    public static int? TryRun(string[] args)
    {
        bool register;
        switch (args.FirstOrDefault())
        {
            case "--register-steamvr":
                register = true;
                break;
            case "--unregister-steamvr":
                register = false;
                break;
            default:
                return null;
        }

        try
        {
            string manifest = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "app.vrmanifest"));
            return register ? Register(manifest) : Unregister(manifest);
        }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException or DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            Console.Out.WriteLine($"SteamVR registration failed: {e.Message}");
            return Failed;
        }
    }

    private static int Register(string manifest)
    {
        string? appKey = SteamVrManifests.AppKey(manifest);
        if (appKey == null)
        {
            Console.Out.WriteLine($"{manifest} has no app key");
            return BadManifest;
        }

        if (!RuntimeInstalled())
        {
            Console.Out.WriteLine("SteamVR is not installed");
            return NotInstalled;
        }

        SteamVrManifestPlan plan = SteamVrManifests.Plan(manifest, appKey, SteamVrManifests.Registered(SteamVrManifests.OpenVrPathsFile), SteamVrManifests.AppKey);
        if (plan.Registered && plan.Rivals.Count == 0)
        {
            Console.Out.WriteLine($"{manifest} is already the registered {appKey}");
            return Ok;
        }

        return WithOpenVr(applications =>
        {
            foreach (string rival in plan.Rivals)
            {
                Console.Out.WriteLine($"Removing other {appKey} copy {rival}: {applications.RemoveApplicationManifest(rival)}");
            }

            EVRApplicationError result = applications.AddApplicationManifest(manifest, bTemporary: false);
            Console.Out.WriteLine($"Registering {manifest}: {result}");
            return result == EVRApplicationError.None ? Ok : Failed;
        });
    }

    private static int Unregister(string manifest)
    {
        string? registered = SteamVrManifests.Registered(SteamVrManifests.OpenVrPathsFile).FirstOrDefault(p => SteamVrManifests.SamePath(p, manifest));
        if (registered == null)
        {
            Console.Out.WriteLine($"{manifest} is not registered with SteamVR");
            return Ok;
        }

        if (!RuntimeInstalled())
        {
            Console.Out.WriteLine("SteamVR is not installed");
            return NotInstalled;
        }

        return WithOpenVr(applications =>
        {
            EVRApplicationError result = applications.RemoveApplicationManifest(registered);
            Console.Out.WriteLine($"Unregistering {registered}: {result}");
            return result == EVRApplicationError.None ? Ok : Failed;
        });
    }

    private static bool RuntimeInstalled()
    {
        try
        {
            return OpenVR.IsRuntimeInstalled();
        }
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            Console.Out.WriteLine($"OpenVR is not available: {e.Message}");
            return false;
        }
    }

    private static int WithOpenVr(Func<CVRApplications, int> action)
    {
        EVRInitError error = EVRInitError.None;
        OpenVR.Init(ref error, EVRApplicationType.VRApplication_Utility);
        if (error != EVRInitError.None)
        {
            Console.Out.WriteLine($"SteamVR could not be reached: {error}");
            return Unreachable;
        }

        try
        {
            return action(OpenVR.Applications);
        }
        finally
        {
            OpenVR.Shutdown();
        }
    }
}
