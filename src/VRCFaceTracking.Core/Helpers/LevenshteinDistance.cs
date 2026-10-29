namespace VRCFaceTracking.Core.Helpers;

public static class LevenshteinDistance
{
    public static int Calculate(string source1, string source2)
    {
        if (source1.Length == 0)
        {
            return source2.Length;
        }

        if (source2.Length == 0)
        {
            return source1.Length;
        }

        int[] previous = new int[source2.Length + 1];
        int[] current = new int[source2.Length + 1];
        for (int j = 0; j < previous.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= source1.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= source2.Length; j++)
            {
                int cost = source1[i - 1] == source2[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[source2.Length];
    }
}
