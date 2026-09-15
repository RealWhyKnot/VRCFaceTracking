using System.Runtime.InteropServices;

namespace VRCFaceTracking.Core.Updates;

public static class UpdateAssets
{
    public static readonly string RidSuffix =
        (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux")
        + "-" + (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64");

    public static readonly string ExeName = OperatingSystem.IsWindows() ? "VRCFaceTracking.exe" : "VRCFaceTracking";

    public static string BaseName(string tag)
    {
        var version = tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;
        return $"VRCFaceTracking-{version}-{RidSuffix}";
    }

    public static string ArchiveName(string tag) => BaseName(tag) + (OperatingSystem.IsWindows() ? ".zip" : ".tar.gz");

    public static string IntegrityName(string tag) => BaseName(tag) + ".integrity.tsv";

    public static (string Sha256, long Size) ParseArchiveEntry(string integrityTsv, string archiveName)
    {
        var line = integrityTsv.Split('\n').Select(l => l.TrimEnd('\r')).FirstOrDefault(l => l.Length > 0)
            ?? throw new InvalidDataException("Integrity file is empty.");
        var fields = line.Split('\t');
        if (fields.Length != 3)
        {
            throw new InvalidDataException($"Integrity row has {fields.Length} fields, expected 3.");
        }

        if (!string.Equals(fields[2], archiveName, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Integrity row names '{fields[2]}', expected '{archiveName}'.");
        }

        var hash = fields[0].Trim().ToLowerInvariant();
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Integrity row hash is not 64 hex characters.");
        }

        if (!long.TryParse(fields[1], out var size) || size <= 0)
        {
            throw new InvalidDataException($"Integrity row size '{fields[1]}' is not a positive number.");
        }

        return (hash, size);
    }

    public static string ResolvePayloadRoot(string extractedDir)
    {
        if (File.Exists(Path.Combine(extractedDir, ExeName)))
        {
            return extractedDir;
        }

        var candidates = Directory.GetDirectories(extractedDir).Where(d => File.Exists(Path.Combine(d, ExeName))).ToArray();
        return candidates.Length == 1
            ? candidates[0]
            : throw new InvalidDataException($"{ExeName} not found in the extracted update.");
    }
}
