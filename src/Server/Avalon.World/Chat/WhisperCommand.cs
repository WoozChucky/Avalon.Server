using Avalon.Domain.Characters;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using Avalon.World.Characters;
using Avalon.World.Social;

namespace Avalon.World.Chat;

/// <summary>
/// /w &lt;player&gt; &lt;message&gt; (alias /whisper, #717): the message to one online character, wherever it is on this world
/// server, on <see cref="ChatChannel.Whisper" /> with the sender's name; the sender gets the same line back with the
/// recipient's name in <see cref="SChatMessagePacket.TargetName" />. Synchronous, on the tick. Refusals are system lines
/// to the sender only, checked in this order: no name or no message, the sender's own name, no online character with
/// that name (offline and unknown alike, so it never reveals who exists). The chat rate limit (#722) is checked
/// after the usage and self refusals and before the lookup, and a whisper counts against it only once delivered.
/// A recipient that ignores the sender (#723) is not sent the line, and the sender cannot tell: it gets its echo and
/// the whisper counts as delivered. Both copies carry the sender's class (#763), since both name the sender.
/// </summary>
public sealed class WhisperCommand(OnlineCharacters online, ChatRateLimiter rateLimiter) : ICommand
{
    public const string Usage = "Usage: /w <player> <message>";
    public const string Self = "You can't whisper yourself.";

    public string Name => "w";
    public string[] Aliases => ["whisper"];

    /// <summary>It reads its text from the message (<see cref="CommandText" />), never the arguments.</summary>
    public bool ReadsRawText => true;

    public static string NotOnline(string name) => $"No player named {name} is online.";

    public void Execute(CommandContext ctx, string[] args)
    {
        if (ctx.Connection.Character is not { } sender)
            return;

        // Slices of the message: the name is looked up in place, and only the text sent on is made a string (#973).
        CommandText.FirstWord(CommandText.AfterCommandWord(ctx.Packet.Message), out ReadOnlySpan<char> name,
            out ReadOnlySpan<char> text);
        if (name.IsEmpty || text.IsEmpty)
        {
            ctx.Reply(Usage);
            return;
        }

        if (CharacterName.Same(name, sender.Name))
        {
            ctx.Reply(Self);
            return;
        }

        if (!rateLimiter.Check(sender.Guid.Id, out TimeSpan retryAfter))
        {
            ctx.Reply(ChatRateLimiter.TooFast(retryAfter));
            return;
        }

        // A name that is not a character name (#936) is nobody's: it is answered as an unknown one, with no lookup.
        if (!CharacterName.IsValid(name) || online.ByName(name) is not { Character: { } recipient } target)
        {
            ctx.Reply(NotOnline(name.ToString()));
            return;
        }

        string message = text.ToString();
        ulong accountId = ctx.Connection.AccountId is { } account ? (ulong)account.Value : 0UL;
        // A recipient ignoring the sender never gets the line (#723); the sender is not told, so the echo below and the
        // rate limit are exactly as for a delivered whisper.
        if (!Ignoring.Hides(target, sender.Guid.Id))
        {
            target.Send(SChatMessagePacket.Create(accountId, sender.Guid.Id, sender.Name, message, ctx.Packet.DateTime,
                PacketEncoder.Shared, ChatChannel.Whisper, characterClass: sender.Class));
        }

        ctx.Connection.Send(SChatMessagePacket.Create(accountId, sender.Guid.Id, sender.Name, message, ctx.Packet.DateTime,
            PacketEncoder.Shared, ChatChannel.Whisper, targetName: recipient.Name,
            characterClass: sender.Class));
        rateLimiter.Record(sender.Guid.Id);
    }
}
