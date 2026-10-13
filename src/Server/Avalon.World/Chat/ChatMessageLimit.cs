using System.Text;

namespace Avalon.World.Chat;

/// <summary>
/// The longest chat message a client may send (#936, owner decision): the whole <c>CChatMessagePacket.Message</c> as
/// sent, command word, name and text together, counted as UTF-8 bytes. A longer one is refused whole, never cut:
/// nothing is said, no command runs, and the sender alone is told (<see cref="TooLong" />).
/// </summary>
internal static class ChatMessageLimit
{
    /// <summary>
    /// The limit, in UTF-8 bytes. It is the game client's own bound, so a client that follows it is never refused: its
    /// chat box takes 255 bytes, and the longest message it builds is a whisper typed after "/w Name", sent as "/w ",
    /// a name of at most 252 bytes, a space and 255 bytes of text. A client that sends more must raise this with it.
    /// </summary>
    public const int MaxBytes = 511;

    /// <summary>The line a sender over the limit is told.</summary>
    public const string TooLong = "Your message is too long and was not sent.";

    /// <summary>
    /// Whether the message is over the limit. One UTF-16 unit is one to three bytes of UTF-8 (a surrogate pair is two
    /// units and four bytes), so most messages are decided by their length alone and the rest are counted, which
    /// allocates nothing for well-formed text. A decoded packet is always well-formed: the decoder turns malformed
    /// bytes into replacement characters, three bytes each, so such a message counts as what it would be sent on as.
    /// A surrogate with no partner, which only a caller inside the server could pass, counts the same way and never
    /// throws (the encoder's fallback allocates 112 B for it).
    /// </summary>
    public static bool Exceeds(string message)
    {
        if (message.Length > MaxBytes)
            return true;
        if (message.Length <= MaxBytes / 3)
            return false;

        return Encoding.UTF8.GetByteCount(message) > MaxBytes;
    }
}
