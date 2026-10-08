using System.Runtime.InteropServices;

namespace VRCFaceTracking.Core.Types;

[StructLayout(LayoutKind.Sequential)]
public struct Vector4 : IEquatable<Vector4>
{
    public float w;
    public float x;
    public float y;
    public float z;

    public Vector4(float w, float x, float y, float z)
    {
        this.w = w;
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public bool Equals(Vector4 other) =>
        w.Equals(other.w) && x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z);

    public override bool Equals(object? obj) => obj is Vector4 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(w, x, y, z);
}