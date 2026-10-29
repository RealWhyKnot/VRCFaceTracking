namespace VRCFaceTracking.Core.Params.Data.Mutation;

internal sealed class EuroFilter
{
    private const float Hz = 10f;

    private float _xPrev;
    private float _dxPrev;

    public float Apply(float x)
    {
        if (float.IsNaN(x))
        {
            return 0f;
        }

        float dx = (x - _xPrev) * Hz;
        float edx = LowPass(ref _dxPrev, dx, Alpha(Filter.DerivativeCutoff));
        float cutoff = Filter.MinCutoff + (Filter.Beta * MathF.Abs(edx));

        return LowPass(ref _xPrev, x, Alpha(cutoff));
    }

    private static float Alpha(float cutoff)
    {
        float tau = 1.0f / (2 * MathF.PI * cutoff);
        float te = 1.0f / Hz;
        return 1.0f / (1.0f + (tau / te));
    }

    private static float LowPass(ref float hatXPrev, float x, float alpha)
    {
        float hatX = (alpha * x) + ((1 - alpha) * hatXPrev);
        hatXPrev = hatX;
        return hatX;
    }
}
