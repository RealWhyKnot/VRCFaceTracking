using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Params.Expressions.Legacy.Eye;
using VRCFaceTracking.Core.Params.Expressions.Legacy.Lip;
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

    /// <summary>
    /// Container of all features and functions that mutates the incoming expression data into output data suitable for driving Unified Expressions.
    /// </summary>
    /// <remarks> Mutates data on update. </remarks>
    public static UnifiedTrackingMutator Mutator = null!;

#pragma warning disable CS0618
    /// <summary>
    /// Version 1 (VRCFaceTracking SRanipal) of all accessible output parameters.
    /// </summary>
    /// <remarks> These parameters are going to be undocumented in the near future and are directly emulated by Version 2 (Unified Expressions) parameters. </remarks>
    public static readonly Parameter[] AllParameters_v1 = LipShapeMerger.AllLipParameters.Union(EyeTrackingParams.ParameterList).ToArray();

    /// <summary>
    /// Version 2 (Unified Expressions) of all accessible output parameters.
    /// </summary>
    public static readonly Parameter[] AllParameters_v2 = UnifiedExpressionsParameters.ExpressionParameters;

    /// <summary>
    /// Head tracking parameters
    /// </summary>
    public static readonly Parameter[] HeadParameters = UnifiedHeadParameters.HeadParameters;

    /// <summary> 
    /// The collection of EVERY possible output parameter
    /// </summary>
    public static readonly Parameter[] AllParameters = AllParameters_v2.Concat(AllParameters_v1).Concat(HeadParameters).ToArray();
#pragma warning restore CS0618

    /// <summary>
    /// Central update action for all expression data to subscribe to.
    /// </summary>
    public static event Action<UnifiedTrackingData> OnUnifiedDataUpdated;

    /// <summary>
    /// Central update function that updates all output parameter data and pushes the latest expressions from VRCFaceTracking modules into the internal expressions buffer.
    /// </summary>
    public static void UpdateData() => OnUnifiedDataUpdated?.Invoke(Mutator.MutateData(Data));
}