namespace VRCFaceTracking.Core.Library;

[Flags]
public enum TrackingCapability
{
    None = 0,
    Eyes = 1,
    Brows = 2,
    Mouth = 4,
    Tongue = 8,
    Head = 16,
    All = Eyes | Brows | Mouth | Tongue | Head,
}
