namespace Avalon.World.Chat;

/// <summary>
/// The raw text of a chat command, read from the message as typed rather than from the dispatcher's arguments:
/// string.Join over those would collapse repeated spaces inside the text.
/// </summary>
internal static class CommandText
{
    /// <summary>
    /// The text as typed after the command word, trimmed at both ends: leading spaces and slashes are skipped, as the
    /// dispatcher skips them, then the command word, then the spaces after it.
    /// </summary>
    public static string AfterCommandWord(string message)
    {
        int i = 0;
        while (i < message.Length && (message[i] == '/' || char.IsWhiteSpace(message[i])))
            i++;
        while (i < message.Length && !char.IsWhiteSpace(message[i]))
            i++;
        return message[i..].Trim();
    }

    /// <summary>
    /// Splits text (already trimmed) into its first word and everything after it, the rest kept as typed apart from
    /// being trimmed at both ends. Either part is empty when missing.
    /// </summary>
    public static (string Word, string After) FirstWord(string text)
    {
        int end = 0;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            end++;
        return (text[..end], text[end..].Trim());
    }
}
