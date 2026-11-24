using System.ComponentModel;
using System.Diagnostics;

namespace VRCFaceTracking.Core.Services;

public sealed class ExternalTrackerDetector
{
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(5);

    private static readonly (string Process, ExternalTrackerKind Kind, string Name)[] Known =
    [
        ("baballonia", ExternalTrackerKind.Baballonia, "Baballonia"),
        ("baballonia.desktop", ExternalTrackerKind.Baballonia, "Baballonia"),
        ("babbleapp", ExternalTrackerKind.Baballonia, "Baballonia"),
        ("eyetrackapp", ExternalTrackerKind.EyeTrackVr, "EyeTrackVR"),
        ("eyetrackvr", ExternalTrackerKind.EyeTrackVr, "EyeTrackVR"),
        ("babble-frame-runtime", ExternalTrackerKind.SteamFrame, "Steam Frame"),
        ("frameeyeosc", ExternalTrackerKind.SteamFrame, "Steam Frame"),
        ("vrcft-steam-frame", ExternalTrackerKind.SteamFrame, "Steam Frame"),
    ];

    private readonly Func<IReadOnlyList<string>> _processNames;
    private readonly TimeProvider _time;
    private readonly TimeSpan _interval;
    private IReadOnlyList<DetectedExternalTracker> _current = [];
    private long _lastRefresh;
    private int _refreshing;

    public ExternalTrackerDetector(Func<IReadOnlyList<string>>? processNames = null, TimeProvider? time = null, TimeSpan? interval = null)
    {
        _processNames = processNames ?? ReadRunningProcessNames;
        _time = time ?? TimeProvider.System;
        _interval = interval ?? DefaultRefreshInterval;
    }

    public IReadOnlyList<DetectedExternalTracker> Current => Volatile.Read(ref _current);

    public bool Refresh()
    {
        IReadOnlyList<DetectedExternalTracker> next = Detect(_processNames());
        Volatile.Write(ref _lastRefresh, _time.GetTimestamp());
        IReadOnlyList<DetectedExternalTracker> previous = Interlocked.Exchange(ref _current, next);
        return !previous.SequenceEqual(next);
    }

    public void Poll()
    {
        long last = Volatile.Read(ref _lastRefresh);
        if (last != 0 && _time.GetElapsedTime(last) < _interval)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                Refresh();
            }
            finally
            {
                Volatile.Write(ref _refreshing, 0);
            }
        });
    }

    public static IReadOnlyList<DetectedExternalTracker> Detect(IEnumerable<string> processNames)
    {
        List<DetectedExternalTracker> found = [];
        foreach (string raw in processNames)
        {
            string name = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw[..^4] : raw;
            foreach ((string process, ExternalTrackerKind kind, string display) in Known)
            {
                if (string.Equals(name, process, StringComparison.OrdinalIgnoreCase) && !found.Exists(f => f.Kind == kind))
                {
                    found.Add(new DetectedExternalTracker(kind, display));
                }
            }
        }

        found.Sort((a, b) => a.Kind.CompareTo(b.Kind));
        return found;
    }

    public static IReadOnlyList<string> ReadRunningProcessNames()
    {
        try
        {
            Process[] processes = Process.GetProcesses();
            List<string> names = new(processes.Length);
            foreach (Process process in processes)
            {
                using (process)
                {
                    names.Add(process.ProcessName);
                }
            }

            return names;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or PlatformNotSupportedException)
        {
            return [];
        }
    }
}
