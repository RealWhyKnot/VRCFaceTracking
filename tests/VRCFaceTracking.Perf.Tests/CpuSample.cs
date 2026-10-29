using System.Runtime.InteropServices;

namespace VRCFaceTracking.Perf.Tests;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct CpuSample(double ElapsedMs, double CpuMs, long AllocatedBytes, int Gen0, int Gen1, int Gen2)
{
    public double PercentOfOneCore => ElapsedMs <= 0 ? 0 : CpuMs / ElapsedMs * 100;
    public double AllocatedKbPerSecond => ElapsedMs <= 0 ? 0 : AllocatedBytes / 1024.0 / (ElapsedMs / 1000);
}
