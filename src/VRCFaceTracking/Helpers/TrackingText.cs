using System.Globalization;
using VRCFaceTracking.Strings;

namespace VRCFaceTracking.Helpers;

internal static class TrackingText
{
    public static string Get(string key, string fallback) =>
        Resources.ResourceManager.GetString(key, Resources.Culture) is { Length: > 0 } text ? text : fallback;

    public static string? Find(string key) =>
        Resources.ResourceManager.GetString(key, Resources.Culture) is { Length: > 0 } text ? text : null;

    public static string Format(string template, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, template, args);

    public static string Ago(DateTimeOffset when, DateTimeOffset now)
    {
        TimeSpan age = now - when;
        return age < TimeSpan.FromSeconds(10) ? Resources.TrackingTime_JustNow
            : age < TimeSpan.FromMinutes(1) ? Format(Resources.TrackingTime_Seconds, (int)age.TotalSeconds)
            : age < TimeSpan.FromHours(1) ? Format(Resources.TrackingTime_Minutes, (int)age.TotalMinutes)
            : age < TimeSpan.FromDays(1) ? Format(Resources.TrackingTime_Hours, (int)age.TotalHours)
            : when.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }
}
