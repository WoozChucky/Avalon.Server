namespace Avalon.Common.Utils;

/// <summary>
/// Packs a SemVer string into a uint32 using an 8-8-16 bit layout:
///   bits 31-24: major (0-255)
///   bits 23-16: minor (0-255)
///   bits 15-0:  patch (0-65535)
///
/// Because the field widths are monotone, uint ordering preserves SemVer ordering:
///   Pack("1.0.0") > Pack("0.9.9")  →  "minimum version" checks are a plain &lt; comparison.
///
/// Examples:
///   "1.0.0" → 0x01_00_0000 (16,777,216)
///   "0.0.1" → 0x00_00_0001 (1)
/// </summary>
public static class SemVerPacker
{
    /// <summary>
    /// Packs a trusted SemVer string (e.g. from validated configuration).
    /// Throws <see cref="FormatException"/> or <see cref="OverflowException"/> on invalid input.
    /// </summary>
    public static uint Pack(string semver)
    {
        ReadOnlySpan<char> span = semver;

        int firstDot = span.IndexOf('.');
        int lastDot = span.LastIndexOf('.');

        uint major = uint.Parse(span[..firstDot]);
        uint minor = uint.Parse(span[(firstDot + 1)..lastDot]);
        uint patch = uint.Parse(span[(lastDot + 1)..]);

        return (major << 24) | (minor << 16) | patch;
    }

    /// <summary>
    /// Tries to pack an untrusted SemVer string (e.g. from a client packet).
    /// Returns <c>false</c> for null, empty, malformed, or out-of-range input.
    /// </summary>
    public static bool TryPack(string? semver, out uint result)
    {
        result = 0;

        if (string.IsNullOrEmpty(semver))
            return false;

        ReadOnlySpan<char> span = semver;
        int firstDot = span.IndexOf('.');
        int lastDot = span.LastIndexOf('.');

        if (firstDot < 0 || lastDot == firstDot)
            return false;

        if (!uint.TryParse(span[..firstDot], out uint major) || major > 255)
            return false;

        if (!uint.TryParse(span[(firstDot + 1)..lastDot], out uint minor) || minor > 255)
            return false;

        if (!uint.TryParse(span[(lastDot + 1)..], out uint patch) || patch > 65535)
            return false;

        result = (major << 24) | (minor << 16) | patch;
        return true;
    }
}
