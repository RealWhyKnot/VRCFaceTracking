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

        var trimmed = text.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        var dash = trimmed.IndexOf('-');
        var numeric = dash < 0 ? trimmed : trimmed[..dash];
        var suffix = dash < 0 ? string.Empty : trimmed[(dash + 1)..];
        var parts = numeric.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        var values = new int[4];
        for (var i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], out values[i]) || values[i] < 0)
            {
                return false;
            }
        }

        version = new AppVersion(values[0], values[1], values[2], values[3], suffix.Equals("beta", StringComparison.OrdinalIgnoreCase));
        return true;
    }

    public int CompareTo(AppVersion other)
    {
        var numeric = (Year, Month, Day, Revision).CompareTo((other.Year, other.Month, other.Day, other.Revision));
        return numeric != 0 ? numeric : (IsBeta ? 0 : 1).CompareTo(other.IsBeta ? 0 : 1);
    }

    public override string ToString() => $"{Year}.{Month}.{Day}.{Revision}{(IsBeta ? "-beta" : string.Empty)}";
}
