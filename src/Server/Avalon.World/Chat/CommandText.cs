namespace Avalon.World.Chat;

/// <summary>
/// The raw text of a chat command, read from the message as typed rather than from the dispatcher's arguments:
/// string.Join over those would collapse repeated spaces inside the text. Slices of the message, so reading it makes no
/// string: a command makes one of the part it sends on, once (#973).
/// </summary>
internal static class CommandText
{
    /// <summary>
    /// The text as typed after the command word, trimmed at both ends: leading spaces and slashes are skipped, as the
    /// dispatcher skips them, then the command word, then the spaces after it.
    /// </summary>
    public static ReadOnlySpan<char> AfterCommandWord(string message)
    {
        int i = 0;
        while (i < message.Length && (message[i] == '/' || char.IsWhiteSpace(message[i])))
            i++;
        while (i < message.Length && !char.IsWhiteSpace(message[i]))
            i++;
        return message.AsSpan(i).Trim();
    }

    /// <summary>
    /// Splits text (already trimmed) into its first word and everything after it, the rest kept as typed apart from
    /// being trimmed at both ends. Either part is empty when missing.
    /// </summary>
    public static void FirstWord(ReadOnlySpan<char> text, out ReadOnlySpan<char> word, out ReadOnlySpan<char> after)
    {
        int end = 0;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            end++;
        word = text[..end];
        after = text[end..].Trim();
    }
}
