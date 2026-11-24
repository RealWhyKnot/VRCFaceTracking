using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data;

public sealed class SignalCapabilitySnapshot
{
    private const int ShapeCount = (int)UnifiedExpressions.Max;

    private readonly bool[] _live;
    private readonly bool[] _module;

    internal SignalCapabilitySnapshot(bool[] live, bool eyeLeftLive, bool eyeRightLive, float eyeRestLeft, float eyeRestRight, int sampleCount, long version, bool[]? externalLive = null, bool externalEyeLeft = false, bool externalEyeRight = false)
    {
        _module = live;
        _live = externalLive is null ? live : Combine(live, externalLive);
        ModuleEyeLeftLive = eyeLeftLive;
        ModuleEyeRightLive = eyeRightLive;
        EyeLeftLive = eyeLeftLive || externalEyeLeft;
        EyeRightLive = eyeRightLive || externalEyeRight;
        EyeRestLeft = eyeRestLeft;
        EyeRestRight = eyeRestRight;
        SampleCount = sampleCount;
        Version = version;
    }

    public static SignalCapabilitySnapshot Empty { get; } = new(new bool[ShapeCount], eyeLeftLive: false, eyeRightLive: false, eyeRestLeft: 1f, eyeRestRight: 1f, sampleCount: 0, version: 0);

    public long Version { get; }

    public int SampleCount { get; }

    public bool HasData => SampleCount > 0;

    public bool ModuleEyeLeftLive { get; }

    public bool ModuleEyeRightLive { get; }

    public bool EyeLeftLive { get; }

    public bool EyeRightLive { get; }

    public bool EyeLive => EyeLeftLive || EyeRightLive;

    public float EyeRestLeft { get; }

    public float EyeRestRight { get; }

    public int LiveShapeCount
    {
        get
        {
            int count = 0;
            foreach (bool live in _live)
            {
                if (live)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public bool IsLive(UnifiedExpressions shape)
    {
        int index = (int)shape;
        return (uint)index < (uint)_live.Length && _live[index];
    }

    public bool IsModuleLive(UnifiedExpressions shape)
    {
        int index = (int)shape;
        return (uint)index < (uint)_module.Length && _module[index];
    }

    public bool IsLive(UnifiedExpressions left, UnifiedExpressions right) => IsLive(left) || IsLive(right);

    public bool SameLiveSet(SignalCapabilitySnapshot other) =>
        EyeLeftLive == other.EyeLeftLive
        && EyeRightLive == other.EyeRightLive
        && _live.AsSpan().SequenceEqual(other._live);

    private static bool[] Combine(bool[] module, bool[] external)
    {
        bool[] combined = new bool[module.Length];
        for (int i = 0; i < combined.Length; i++)
        {
            combined[i] = module[i] || (i < external.Length && external[i]);
        }

        return combined;
    }
}
