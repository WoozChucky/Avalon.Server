using System.Text;

namespace Avalon.Crashes.Validation;

// Cache by source slice, not basename: distinct modules may legitimately share a display name.
internal sealed class MinidumpNameReader
{
    private static readonly UnicodeEncoding s_utf16 = new(false, false, true);
    private static readonly UTF8Encoding s_utf8 = new(false, true);
    private readonly Dictionary<(uint Offset, int Length, bool Utf16), MinidumpName> _names = [];
    private int _decodedBytes;

    internal MinidumpName Read(ReadOnlySpan<byte> bytes, uint offset, bool utf16)
    {
        (uint Offset, int Length, bool Utf16) key = (offset, bytes.Length, utf16);
        if (_names.TryGetValue(key, out MinidumpName cached))
            return cached;
        // A file-sized aggregate budget prevents overlapping/aliased slices from amplifying decoded allocation.
        // Repeated references to the exact same slice are decoded only once and do not consume this budget again.
        if (bytes.Length > CrashLimits.ArchiveBytes - _decodedBytes)
            throw new CrashInputException(CrashFailureCode.ResourceLimit);
        _decodedBytes += bytes.Length;
        string path;
        try
        {
            path = utf16 ? s_utf16.GetString(bytes) : s_utf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw Invalid();
        }
        if (path.Contains('\0', StringComparison.Ordinal))
            throw Invalid();
        ReadOnlySpan<char> remaining = path;
        while (!remaining.IsEmpty)
        {
            int separator = remaining.IndexOfAny('/', '\\');
            ReadOnlySpan<char> segment = separator < 0 ? remaining : remaining[..separator];
            if (segment.SequenceEqual(".."))
                throw Invalid();
            if (separator < 0)
                break;
            remaining = remaining[(separator + 1)..];
        }
        int lastSeparator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        string name = path[(lastSeparator + 1)..];
        if (name.Length == 0 || name is "." or ".." || name.Any(c => c < 32 || c is ':' or '<' or '>' or '"' or '|' or '?' or '*'))
            throw Invalid();
        var parsed = new MinidumpName(name, lastSeparator < 0);
        _names.Add(key, parsed);
        return parsed;
    }

    private static CrashInputException Invalid() => new(CrashFailureCode.InvalidDump);
}

internal readonly record struct MinidumpName(string Basename, bool IsBareName);
