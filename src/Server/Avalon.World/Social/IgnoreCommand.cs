using Avalon.Database.Character.Repositories;
using Avalon.World.Characters;
using Avalon.World.Chat;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Microsoft.Extensions.Options;

namespace Avalon.World.Social;

/// <summary>
/// /ignore &lt;name&gt; (#723): adds a character of this world, online or not, to the caller's ignore list. Synchronous
/// on the tick: an online name is found in <see cref="OnlineCharacters" />, and only an offline one is looked up in the
/// Character DB, through <see cref="CommandContext.Then{T}" />, whose callback re-checks everything on the tick. Refusals
/// are system lines, in this order: no single name, the caller's own name, a name already on the list, a full list
/// (<c>Game:MaxIgnoredCharacters</c>), then, once the character is found, no such character, the caller itself, a
/// character already on the list (under another name), a full list. An accepted one answers a line and sends the whole
/// list (SMSG_IGNORE_LIST).
/// </summary>
public sealed class IgnoreCommand(
    OnlineCharacters online,
    ICharacterIgnoreRepository repository,
    IOptions<GameConfiguration> options,
    TimeProvider time) : ICommand
{
    public string Name => "ignore";
    public string[] Aliases => [];

    public void Execute(CommandContext ctx, string[] args)
    {
        if (ctx.Connection.Character is not CharacterEntity owner)
            return;

        if (args.Length != 1)
        {
            ctx.Reply(IgnoreLines.IgnoreUsage);
            return;
        }

        string name = args[0].Trim();
        int max = options.Value.MaxIgnoredCharacters;
        if (string.Equals(name, owner.Name, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Reply(IgnoreLines.Self);
            return;
        }

        if (owner.Ignores.FindByName(name) is { } listed)
        {
            ctx.Reply(IgnoreLines.Already(listed.Name));
            return;
        }

        if (owner.Ignores.Count >= max)
        {
            ctx.Reply(IgnoreLines.Full(max));
            return;
        }

        if (online.ByName(name)?.Character is { } target)
        {
            TryAdd(ctx, owner, target.Guid.Id, target.Name);
            return;
        }

        ctx.Then(repository.FindCharacterByNameAsync(name, CancellationToken.None), match =>
        {
            // The lookup finished a tick or more later: the caller may have left the world, or changed character.
            if (!ReferenceEquals(ctx.Connection.Character, owner))
                return;

            if (match is null)
            {
                ctx.Reply(IgnoreLines.NoSuchCharacter(name));
                return;
            }

            TryAdd(ctx, owner, match.Id.Value, match.Name);
        });
    }

    private void TryAdd(CommandContext ctx, CharacterEntity owner, uint id, string name)
    {
        int max = options.Value.MaxIgnoredCharacters;
        if (id == owner.Guid.Id)
        {
            ctx.Reply(IgnoreLines.Self);
            return;
        }

        if (owner.Ignores.Contains(id))
        {
            ctx.Reply(IgnoreLines.Already(owner.Ignores.Entries.First(e => e.Id == id).Name));
            return;
        }

        if (owner.Ignores.Count >= max)
        {
            ctx.Reply(IgnoreLines.Full(max));
            return;
        }

        owner.Ignores.Add(id, name, time.GetUtcNow().UtcDateTime);
        ctx.Connection.Send(owner.Ignores.ToPacket(ctx.Connection.CryptoSession.Encrypt));
        ctx.Reply(IgnoreLines.Added(name));
    }
}
