using System.Runtime.InteropServices;

namespace VRCFaceTracking.Core.Services;

[StructLayout(LayoutKind.Auto)]
public readonly record struct ExternalOscLid(ExternalOscLidSide Side, ExternalOscLidFormat Format)
{
    private const float UnifiedOpenShare = 0.75f;
    private const float ExpandedOpenShare = 0.8f;

    public ExternalOscLidValue Decode(float value) => Format switch
    {
        ExternalOscLidFormat.Unified => Split(Math.Clamp(value, 0f, 1f), UnifiedOpenShare, squint: null),
        ExternalOscLidFormat.Expanded => Split(Math.Clamp(value, 0f, 1f), ExpandedOpenShare, squint: null),
        ExternalOscLidFormat.ExpandedSqueeze => DecodeSqueeze(Math.Clamp(value, -1f, 1f)),
        ExternalOscLidFormat.Closed => Closed(value),
        _ => Closed(value),
    };

    private static ExternalOscLidValue Closed(float value) => new(1f - Math.Clamp(value, 0f, 1f), Widen: null, Squint: null);

    private static ExternalOscLidValue DecodeSqueeze(float clamped) =>
        clamped < 0f
            ? new ExternalOscLidValue(0f, 0f, -clamped)
            : Split(clamped, ExpandedOpenShare, squint: 0f);

    private static ExternalOscLidValue Split(float clamped, float openShare, float? squint)
    {
        float openness = Math.Clamp(clamped / openShare, 0f, 1f);
        float widen = clamped > openShare ? Math.Clamp((clamped - openShare) / (1f - openShare), 0f, 1f) : 0f;
        return new ExternalOscLidValue(openness, widen, squint);
    }
}
