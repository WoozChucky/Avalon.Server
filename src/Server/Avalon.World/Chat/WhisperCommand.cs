using Avalon.Network.Packets.Social;
using Avalon.World.Characters;

namespace Avalon.World.Chat;

/// <summary>
/// /w &lt;player&gt; &lt;message&gt; (alias /whisper, #717): the message to one online character, wherever it is on this world
/// server, on <see cref="ChatChannel.Whisper" /> with the sender's name; the sender gets the same line back with the
/// recipient's name in <see cref="SChatMessagePacket.TargetName" />. Synchronous, on the tick. Refusals are system lines
/// to the sender only, checked in this order: no name or no message, the sender's own name, no online character with
/// that name (offline and unknown alike, so it never reveals who exists).
/// </summary>
public sealed class WhisperCommand(OnlineCharacters online) : ICommand
{
    public const string Usage = "Usage: /w <player> <message>";
    public const string Self = "You can't whisper yourself.";

    public string Name => "w";
    public string[] Aliases => ["whisper"];

    public static string NotOnline(string name) => $"No player named {name} is online.";

    public void Execute(CommandContext ctx, string[] args)
    {
        if (ctx.Connection.Character is not { } sender)
            return;

        (string name, string message) = CommandText.FirstWord(CommandText.AfterCommandWord(ctx.Packet.Message));
        if (name.Length == 0 || message.Length == 0)
        {
            ctx.Reply(Usage);
            return;
        }

        if (string.Equals(name, sender.Name, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Reply(Self);
            return;
        }

        if (online.ByName(name) is not { Character: { } recipient } target)
        {
            ctx.Reply(NotOnline(name));
            return;
        }

        ulong accountId = ctx.Connection.AccountId is { } account ? (ulong)account.Value : 0UL;
        target.Send(SChatMessagePacket.Create(accountId, sender.Guid.Id, sender.Name, message, ctx.Packet.DateTime,
            target.CryptoSession.Encrypt, ChatChannel.Whisper));
        ctx.Connection.Send(SChatMessagePacket.Create(accountId, sender.Guid.Id, sender.Name, message, ctx.Packet.DateTime,
            ctx.Connection.CryptoSession.Encrypt, ChatChannel.Whisper, targetName: recipient.Name));
    }
}
