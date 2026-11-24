namespace VRCFaceTracking.Core.Types;

public sealed record CameraControlCommand(string StreamId, CameraControlKind Kind, float Value = 0f, float X = 0f, float Y = 0f, float Width = 0f, float Height = 0f);
