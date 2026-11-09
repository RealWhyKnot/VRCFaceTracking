namespace VRCFaceTracking.Core.Params;

internal static class VoiceActivity
{
    private static VoiceReading? Current;

    internal static VoiceReading? Latest => Volatile.Read(ref Current);

    internal static bool SetLevel(float level)
    {
        VoiceReading? previous = Latest;
        Volatile.Write(ref Current, new VoiceReading(HasLevel: true, level, previous?.Muted ?? false));
        return previous is not { HasLevel: true };
    }

    internal static void SetMuted(bool muted)
    {
        VoiceReading? previous = Latest;
        Volatile.Write(ref Current, new VoiceReading(previous?.HasLevel ?? false, previous?.Level ?? 0f, muted));
    }

    internal static void Clear() => Volatile.Write(ref Current, null);
}
