namespace VRCFaceTracking.Core.Tests;

internal static class Noise
{
    public static byte[] Bytes(int seed, int length)
    {
        byte[] bytes = new byte[length];
        uint state = Next((uint)seed);
        for (int i = 0; i < length; i++)
        {
            state = Next(state);
            bytes[i] = (byte)state;
        }

        return bytes;
    }

    public static int Int(int seed, int maxExclusive) => (int)(Next(Next((uint)seed ^ 0x9E3779B9u)) % (uint)maxExclusive);

    private static uint Next(uint state)
    {
        state = (state * 2654435761u) + 0x6D2B79F5u;
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }
}
