using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking;

/// <summary>
/// Class that contains all relevant data
/// </summary>
public class UnifiedTracking
{
    /// <summary>
    /// Eye image data sent from the loaded eye module.
    /// </summary>
    public static Image EyeImageData = new();

    /// <summary>
    /// Lip / Expression image data sent from the loaded expressions module.
    /// </summary>
    public static Image LipImageData = new();

    /// <summary>
    /// Latest Expression Data accessible and sent by all VRCFaceTracking modules.
    /// </summary>
    public static UnifiedTrackingData Data = new();

    /// <summary>
    /// Guards Data against concurrent module merges and sender snapshots.
    /// </summary>
    public static readonly object DataLock = new();

    public static DataLockScope LockData()
    {
        Monitor.Enter(DataLock);
        return new DataLockScope(DataLock);
    }

    private static int _dataVersion;

    /// <summary>
    /// Increments whenever a module merges new tracking data into <see cref="Data"/>.
    /// </summary>
    public static int DataVersion => Volatile.Read(ref _dataVersion);

    private static long _lastDataUpdateTicks;

    /// <summary>
    /// Timestamp ticks of the most recent module merge, for measuring how stale the sent data is.
    /// </summary>
    public static long LastDataUpdateTicks => Interlocked.Read(ref _lastDataUpdateTicks);

    private static long _lastSampleTicks;

    public static long LastSampleTicks => Interlocked.Read(ref _lastSampleTicks);

    internal static readonly AutoResetEvent DataArrived = new(false);

    /// <summary>
    /// Marks <see cref="Data"/> as carrying values a module has not published yet.
    /// </summary>
    public static void MarkDataUpdated() => MarkDataUpdated(0);

    public static void MarkDataUpdated(long sampleTicks)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _dataVersion);
        Interlocked.Exchange(ref _lastDataUpdateTicks, now);
        Interlocked.Exchange(ref _lastSampleTicks, sampleTicks != 0 ? sampleTicks : now);
        DataArrived.Set();
    }
}
