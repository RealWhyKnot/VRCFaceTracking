using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data;

public static class NativeFaceBlob
{
    public const string Address = "/tracking/face/v1";
    public const int ShapeOffset = 14;
    public const int Length = ShapeOffset + (int)UnifiedExpressions.Max;

    public static void Write(UnifiedTrackingData data, Span<byte> bytes)
    {
        if (bytes.Length < Length)
        {
            throw new ArgumentException($"The face tracking blob needs {Length} bytes, got {bytes.Length}", nameof(bytes));
        }

        UnifiedEyeData eye = data.Eye;
        float rightYaw = eye.Right.Gaze.ToNormalizedYaw();
        float rightUp = -eye.Right.Gaze.ToNormalizedPitch();
        float leftYaw = eye.Left.Gaze.ToNormalizedYaw();
        float leftUp = -eye.Left.Gaze.ToNormalizedPitch();

        bytes[0] = ToByte(rightYaw);
        bytes[1] = ToByte(-rightYaw);
        bytes[2] = ToByte(rightUp);
        bytes[3] = ToByte(-rightUp);
        bytes[4] = ToByte(-leftYaw);
        bytes[5] = ToByte(leftYaw);
        bytes[6] = ToByte(leftUp);
        bytes[7] = ToByte(-leftUp);
        bytes[8] = ToByte(1f - eye.Right.Openness);
        bytes[9] = ToByte(1f - eye.Left.Openness);

        float rightPupil = PupilOffset(eye, eye.Right.PupilDiameter_MM);
        float leftPupil = PupilOffset(eye, eye.Left.PupilDiameter_MM);
        bytes[10] = ToByte(rightPupil);
        bytes[11] = ToByte(leftPupil);
        bytes[12] = ToByte(-rightPupil);
        bytes[13] = ToByte(-leftPupil);

        for (int i = 0; i < (int)UnifiedExpressions.Max; i++)
        {
            bytes[ShapeOffset + i] = ToByte(data.Shapes[i].Weight);
        }
    }

    private static float PupilOffset(UnifiedEyeData eye, float diameter)
    {
        float range = eye._maxDilation - eye._minDilation;
        return !(range > 0f) || float.IsInfinity(range) ? 0f : (((diameter - eye._minDilation) / range) - 0.5f) * 2f;
    }

    private static byte ToByte(float value) =>
        float.IsNaN(value) ? (byte)0 : (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);
}
