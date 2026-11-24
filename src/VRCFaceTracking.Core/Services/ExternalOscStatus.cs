namespace VRCFaceTracking.Core.Services;

public sealed record ExternalOscStatus(ExternalOscState State, int PacketsPerSecond, string? LastError);
