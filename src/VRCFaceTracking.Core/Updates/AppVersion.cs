using System.Globalization;

namespace VRCFaceTracking.Core.Updates;

public readonly record struct AppVersion(int Year, int Month, int Day, int Revision, bool IsBeta) : IComparable<AppVersion>
{
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        int dash = trimmed.IndexOf('-', StringComparison.Ordinal);
        string numeric = dash < 0 ? trimmed : trimmed[..dash];
        string suffix = dash < 0 ? string.Empty : trimmed[(dash + 1)..];
        string[] parts = numeric.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        int[] values = new int[4];
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], CultureInfo.InvariantCulture, out values[i]) || values[i] < 0)
            {
                return false;
            }
        }

        version = new AppVersion(values[0], values[1], values[2], values[3], suffix.Equals("beta", StringComparison.OrdinalIgnoreCase));
        return true;
    }

    public int CompareTo(AppVersion other)
    {
        int numeric = (Year, Month, Day, Revision).CompareTo((other.Year, other.Month, other.Day, other.Revision));
        return numeric != 0 ? numeric : (IsBeta ? 0 : 1).CompareTo(other.IsBeta ? 0 : 1);
    }

    public static bool operator <(AppVersion left, AppVersion right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator <=(AppVersion left, AppVersion right)
    {
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >(AppVersion left, AppVersion right)
    {
        return left.CompareTo(right) > 0;
    }

    public static bool operator >=(AppVersion left, AppVersion right)
    {
        return left.CompareTo(right) >= 0;
    }

    public override string ToString() => $"{Year}.{Month}.{Day}.{Revision}{(IsBeta ? "-beta" : string.Empty)}";
}
