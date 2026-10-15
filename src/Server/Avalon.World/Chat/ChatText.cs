using System.Buffers;
using System.Globalization;

namespace Avalon.World.Chat;

/// <summary>
/// What a player's line may carry (#944, owner decision), for the three that carry a player's text to other players:
/// said (<c>ChatMessageHandler</c>), party (<c>/p</c>) and whispered (<c>/w</c>). Every code point in the Unicode
/// categories Control, Format, LineSeparator and ParagraphSeparator is stripped: C0 and C1 controls (tab and line feed
/// included), bidirectional overrides and isolates, zero-width characters (the zero-width joiner included), the byte
/// order mark, U+2028 and U+2029, and the format characters outside the first plane (the tag characters among them),
/// read as whole surrogate pairs. Everything else stays as sent, emoji included, and a surrogate pair is never split.
/// Then the text is trimmed at both ends, so a line that is empty after both says nothing.
/// </summary>
/// <remarks>
/// The length rule (<see cref="ChatMessageLimit" />) is applied before this, to the message as sent: a message that is
/// too long is refused on what the client sent, never on what stripping would leave of it. Text that has nothing to
/// strip or trim is sent on as it is, without a copy; only text that changes is copied, once.
/// </remarks>
internal static class ChatText
{
    // A message within the length rule is at most 511 UTF-16 units, so the stripped copy is built on the stack. A
    // longer text, which only a caller inside the server could pass, borrows from the pool.
    private const int StackChars = 512;

    /// <summary>
    /// Whether the text says nothing once stripped and trimmed: every code point in it is stripped or white space.
    /// Allocates nothing, so a caller decides with it before it spends anything on the line.
    /// </summary>
    public static bool SaysNothing(ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length;)
        {
            int units = Next(text, i, out bool stripped);
            if (!stripped && !char.IsWhiteSpace(text[i]))
                return false;
            i += units;
        }

        return true;
    }

    /// <summary>The message as it is sent on: stripped, then trimmed. The message itself when neither changes it.</summary>
    public static string Clean(string message) =>
        IndexOfStripped(message) < 0 ? message.Trim() : Stripped(message);

    /// <summary>The text as it is sent on: stripped, then trimmed, as one new string.</summary>
    public static string Clean(ReadOnlySpan<char> text) =>
        IndexOfStripped(text) < 0 ? text.Trim().ToString() : Stripped(text);

    private static int IndexOfStripped(ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length;)
        {
            int units = Next(text, i, out bool stripped);
            if (stripped)
                return i;
            i += units;
        }

        return -1;
    }

    private static string Stripped(ReadOnlySpan<char> text)
    {
        char[]? rented = null;
        Span<char> kept = text.Length <= StackChars
            ? stackalloc char[StackChars]
            : (rented = ArrayPool<char>.Shared.Rent(text.Length));

        int length = 0;
        for (int i = 0; i < text.Length;)
        {
            int units = Next(text, i, out bool stripped);
            if (!stripped)
            {
                text.Slice(i, units).CopyTo(kept[length..]);
                length += units;
            }

            i += units;
        }

        string result = new(((ReadOnlySpan<char>)kept[..length]).Trim());
        if (rented is not null)
            ArrayPool<char>.Shared.Return(rented);
        return result;
    }

    /// <summary>
    /// The code point at <paramref name="i" />: how many UTF-16 units it takes (2 for a whole surrogate pair, else 1)
    /// and whether it is stripped. A surrogate without its partner, which a decoded packet never holds, is kept.
    /// </summary>
    private static int Next(ReadOnlySpan<char> text, int i, out bool stripped)
    {
        char c = text[i];
        if (c < 0x80)
        {
            // ASCII: the controls are U+0000 to U+001F and U+007F, and nothing else in it is stripped.
            stripped = c < 0x20 || c == 0x7F;
            return 1;
        }

        if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
        {
            stripped = IsStripped(CharUnicodeInfo.GetUnicodeCategory(char.ConvertToUtf32(c, text[i + 1])));
            return 2;
        }

        stripped = IsStripped(CharUnicodeInfo.GetUnicodeCategory(c));
        return 1;
    }

    private static bool IsStripped(UnicodeCategory category) => category is UnicodeCategory.Control
        or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
