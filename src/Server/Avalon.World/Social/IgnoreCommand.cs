using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.World.Characters;
using Avalon.World.Chat;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Parties;
using Microsoft.Extensions.Options;

namespace Avalon.World.Social;

/// <summary>
/// /ignore &lt;name&gt; (#723): adds a character of this world, online or not, to the caller's ignore list. Synchronous
/// on the tick: an online name is found in <see cref="OnlineCharacters" />, and only an offline one is looked up in the
/// Character DB, through <see cref="CommandContext.Then{T}" />, whose callback re-checks everything on the tick. Refusals
/// are system lines, in this order: no single name, the caller's own name, a name already on the list, a full list
/// (<c>Game:MaxIgnoredCharacters</c>), the chat rate limit (#722, which every /ignore past these spends), then, once the character is found, no such character, the caller itself, a
/// character already on the list (under another name), a full list. An accepted one answers a line and sends the whole
/// list (SMSG_IGNORE_LIST); an invite from that character the caller still holds ends silently
/// (<see cref="PartyService.HideInviteFrom" />).
/// </summary>
public sealed class IgnoreCommand(
    OnlineCharacters online,
    ICharacterIgnoreRepository repository,
    IOptions<GameConfiguration> options,
    TimeProvider time,
    ChatRateLimiter rateLimiter,
    PartyService parties) : ICommand
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
        if (RefusedInMemory(ctx, owner, name))
            return;

        // Owner decision: /ignore spends the chat budget (#722) like a chat message, so it cannot be used to make a
        // database query per tick. Checked after the refusals made in memory, which spend nothing, and spent here,
        // before the lookup, whatever the lookup finds.
        if (!rateLimiter.Check(owner.Guid.Id, out TimeSpan retryAfter))
        {
            ctx.Reply(ChatRateLimiter.TooFast(retryAfter));
            return;
        }

        rateLimiter.Record(owner.Guid.Id);

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

    /// <summary>The refusals that read only the list: the caller's own name, a name already listed, a full list.</summary>
    private bool RefusedInMemory(CommandContext ctx, CharacterEntity owner, string name)
    {
        int max = options.Value.MaxIgnoredCharacters;
        if (CharacterName.Same(name, owner.Name))
        {
            ctx.Reply(IgnoreLines.Self);
            return true;
        }

        if (owner.Ignores.FindByName(name) is { } listed)
        {
            ctx.Reply(IgnoreLines.Already(listed.Name));
            return true;
        }

        if (owner.Ignores.Count >= max)
        {
            ctx.Reply(IgnoreLines.Full(max));
            return true;
        }

        return false;
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
        // Owner decision: an invite from that character already pending ends silently, as one sent now would.
        parties.HideInviteFrom(owner.Guid.Id, id);
        ctx.Connection.Send(owner.Ignores.ToPacket(ctx.Connection.CryptoSession.Encrypt));
        ctx.Reply(IgnoreLines.Added(name));
    }
}
