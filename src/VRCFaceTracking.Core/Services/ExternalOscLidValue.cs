using System.Runtime.InteropServices;

namespace VRCFaceTracking.Core.Services;

[StructLayout(LayoutKind.Auto)]
public readonly record struct ExternalOscLidValue(float Openness, float? Widen, float? Squint);
