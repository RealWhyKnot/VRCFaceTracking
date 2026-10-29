namespace VRCFaceTracking.Core.Osc.DataTypes;

internal static class ParamAddressMatcher
{
    internal static bool Matches(string address, string paramName) =>
        Matches(address, paramName, trailingDigits: false);

    internal static bool MatchesIndexed(string address, string paramName) =>
        Matches(address, paramName, trailingDigits: true);

    private static bool Matches(string address, string paramName, bool trailingDigits)
    {
        if (string.IsNullOrEmpty(address) || string.IsNullOrEmpty(paramName))
        {
            return false;
        }

        int end = address.Length;
        if (trailingDigits)
        {
            int digits = 0;
            while (end > 0 && char.IsAsciiDigit(address[end - 1]))
            {
                end--;
                digits++;
            }

            if (digits == 0)
            {
                return false;
            }
        }

        int start = end - paramName.Length;
        if (start < 0 || string.CompareOrdinal(address, start, paramName, 0, paramName.Length) != 0)
        {
            return false;
        }

        if (start == 0)
        {
            return true;
        }

        if (address[start - 1] != '/')
        {
            return false;
        }

        int cursor = start - 1;
        int versionDigits = 0;
        while (cursor > 0 && char.IsAsciiDigit(address[cursor - 1]))
        {
            cursor--;
            versionDigits++;
        }

        return versionDigits == 0 || cursor == 0 || address[cursor - 1] != 'v';
    }
}
