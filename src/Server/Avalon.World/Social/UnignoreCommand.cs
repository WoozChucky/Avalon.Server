using Avalon.World.Chat;
using Avalon.World.Entities;

namespace Avalon.World.Social;

/// <summary>
/// /unignore &lt;name&gt; (#723): takes a character off the caller's ignore list, by the name the list shows (ignoring
/// case). In memory only, on the tick. Answers a line, and after a change sends the whole list.
/// </summary>
public sealed class UnignoreCommand : ICommand
{
    public string Name => "unignore";
    public string[] Aliases => [];

    public void Execute(CommandContext ctx, string[] args)
    {
        if (ctx.Connection.Character is not CharacterEntity owner)
            return;

        if (args.Length != 1)
        {
            ctx.Reply(IgnoreLines.UnignoreUsage);
            return;
        }

        if (owner.Ignores.FindByName(args[0]) is not { } entry)
        {
            ctx.Reply(IgnoreLines.NotOnList(args[0].Trim()));
            return;
        }

        owner.Ignores.Remove(entry.Id);
        ctx.Connection.Send(owner.Ignores.ToPacket(ctx.Connection.CryptoSession.Encryptor));
        ctx.Reply(IgnoreLines.Removed(entry.Name));
    }
}
