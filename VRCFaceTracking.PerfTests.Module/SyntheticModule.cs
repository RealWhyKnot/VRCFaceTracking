using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace VRCFaceTracking.PerfTests.Module;

public sealed class SyntheticModule : ExtTrackingModule
{
    public const string ModeVariable = "VRCFT_PERF_MODULE_MODE";
    public const string PidFileVariable = "VRCFT_PERF_MODULE_PIDFILE";
    public const string StreamMode = "stream";
    public const string IdleMode = "idle";
    public const int StreamHz = 90;
    public const int IdleUpdateMs = 50;

    private Array _shapes = Array.Empty<float>();
    private bool _stream;
    private long _frame;
    private long _nextFrameTicks;

    public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

    public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
    {
        var tracking = Type.GetType("VRCFaceTracking.UnifiedTracking, VRCFaceTracking.Core", throwOnError: true)!;
        var data = tracking.GetField("Data", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        _shapes = (Array)data.GetType().GetField("Shapes")!.GetValue(data)!;
        if (Marshal.SizeOf(_shapes.GetType().GetElementType()!) != sizeof(float))
        {
            throw new InvalidOperationException("Unexpected shape layout");
        }

        _stream = Environment.GetEnvironmentVariable(ModeVariable) != IdleMode;
        if (Environment.GetEnvironmentVariable(PidFileVariable) is { Length: > 0 } pidFile)
        {
            File.WriteAllText(pidFile, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        _nextFrameTicks = Stopwatch.GetTimestamp();
        ModuleInformation.Name = "Synthetic Perf Module";
        ModuleInformation.StaticImages = new List<Stream>();
        return (eyeAvailable, expressionAvailable);
    }

    public override void Update()
    {
        if (!_stream)
        {
            Thread.Sleep(IdleUpdateMs);
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (_nextFrameTicks > now)
        {
            Thread.Sleep(TimeSpan.FromTicks((_nextFrameTicks - now) * TimeSpan.TicksPerSecond / Stopwatch.Frequency));
        }
        _nextFrameTicks += Stopwatch.Frequency / StreamHz;

        _frame++;
        ref var first = ref Unsafe.As<byte, float>(ref MemoryMarshal.GetArrayDataReference(_shapes));
        var weight = (_frame % 100) / 100f;
        for (var i = 0; i < _shapes.Length; i++)
        {
            Unsafe.Add(ref first, i) = weight;
        }
    }

    public override void Teardown()
    {
    }
}
