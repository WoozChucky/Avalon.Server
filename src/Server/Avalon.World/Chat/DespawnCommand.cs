using Avalon.Common.Accounts;
using Avalon.World.Entities;
using Avalon.World.Items;
using Avalon.World.Public.Creatures;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Chat;

/// <summary>
/// <c>/despawn</c> (GameMaster): removes what <c>/spawn</c> placed in the caller's instance and is still alive, through
/// the instance's summon expiry (a zero lifetime: dropped from its encounter, then removed on the next tick). A test
/// starts from the creatures it spawns, whatever an earlier run left in a shared instance.
/// </summary>
public sealed class DespawnCommand(IWorld world, GmSpawns spawns, ILogger<DespawnCommand> logger) : ICommand
{
    public string Name => "despawn";
    public string[] Aliases => [];
    public AccountAccessLevel RequiredAccess => AccessLevels.GameMaster;

    public void Execute(CommandContext ctx, string[] args)
    {
        if (args.Length != 0)
        {
            ctx.Refuse("Usage: /despawn");
            return;
        }

        if (ctx.Connection.Character is not CharacterEntity character)
        {
            ctx.Refuse("No character selected.");
            return;
        }

        if (world.InstanceRegistry.GetInstanceById(character.InstanceId) is not IItemUseHost host)
        {
            ctx.Refuse("You are not in a map.");
            return;
        }

        spawns.ForgetGone(world.InstanceRegistry);
        List<ICreature> standing = spawns.Take(character.InstanceId);
        foreach (ICreature creature in standing)
            host.DespawnAfter(creature, TimeSpan.Zero);
        int removed = standing.Count;

        logger.LogInformation("Account {AccountId} despawned {Removed} spawned creatures from instance {InstanceId}",
            ctx.Connection.AccountId, removed, character.InstanceId);
        ctx.Reply(removed == 0 ? "No spawned creatures here." : $"Despawned {removed}.");
    }
}
