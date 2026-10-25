namespace Avalon.Crashes;

internal static class CrashText
{
    // PostgreSQL text and jsonb cannot represent U+0000. Normalize decoded strings,
    // before signatures/serialization, so literal backslash-u text remains unchanged.
    internal static string Sanitize(string value, ref CrashQualityFlags quality)
    {
        if (!value.Contains('\0'))
            return value;
        quality |= CrashQualityFlags.SanitizedText;
        return value.Replace('\0', '\ufffd');
    }
}
