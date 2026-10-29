using VRCFaceTracking.Core.Params.Data;

namespace VRCFaceTracking.Core.Params.Expressions.Legacy.Lip;

internal sealed class PositiveNegativeShape : ICombinedShape
{
    private readonly SRanipal_LipShape_v2 _positiveShape, _negativeShape;
    private float _positiveCache, _negativeCache;
    private readonly bool _steps;

    internal PositiveNegativeShape(SRanipal_LipShape_v2 positiveShape, SRanipal_LipShape_v2 negativeShape, bool steps = false)
    {
        _positiveShape = positiveShape;
        _negativeShape = negativeShape;
        _steps = steps;
    }

    public float GetBlendedLipShape(UnifiedTrackingData inputMap)
    {
        _positiveCache = UnifiedSRanMapper.GetTransformedShape(_positiveShape, inputMap);
        _negativeCache = UnifiedSRanMapper.GetTransformedShape(_negativeShape, inputMap) * -1;
        return _steps ? _positiveCache - _negativeCache - 1 : _positiveCache + _negativeCache;
    }
}
