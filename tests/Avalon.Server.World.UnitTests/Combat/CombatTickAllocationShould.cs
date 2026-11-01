using Avalon.Common;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Testing;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// The combat half of a steady tick allocates nothing: the encounter update and the threat broadcast
/// run every tick of every instance, with an encounter live and a player targeting its creature, or targeting a unit in
/// its view that gets no list (#1269): a creature in no encounter, or a character (here itself).
/// </summary>
public class CombatTickAllocationShould
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    [Theory]
    [InlineData("creature in its encounter", 1)]
    [InlineData("creature in no encounter", 0)]
    [InlineData("character", 0)]
    public void Run_a_steady_combat_tick_without_allocating(string target, int sent)
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2001, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var config = new CombatConfig();
        var registry = new EncounterRegistry(config, clock);
        var combat = new CombatService(config, registry, time: clock);
        var broadcast = new ThreatBroadcastService(config, clock);

        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 9_001),
            Metadata = Substitute.For<ICreatureMetadata>(),
            Health = 100,
            CurrentHealth = 100,
        };
        var idle = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 9_003),
            Metadata = Substitute.For<ICreatureMetadata>(),
            Health = 100,
            CurrentHealth = 100,
        };
        CharacterEntity player = TestCharacters.New(9_002);
        combat.EnterCombat(creature, player);
        ((Encounter)registry.Active.Single()).AddThreat(creature, player, 1_000_000f);

        ulong targetGuid = target switch
        {
            "creature in its encounter" => creature.Guid.RawValue,
            "creature in no encounter" => idle.Guid.RawValue,
            _ => player.Guid.RawValue,
        };
        var connection = new QuietConnection(player) { CurrentTargetGuid = targetGuid };
        var connections = new Dictionary<ObjectGuid, IWorldConnection> { [player.Guid] = connection };
        var creatures = new Dictionary<ObjectGuid, ICreature> { [creature.Guid] = creature, [idle.Guid] = idle };
        // The threat list is mirrored only for a target in the player's view (#1269).
        player.CharacterGameState.Update(player.Guid, player.Position, new InterestRange(50f, 10f), creatures,
            new Dictionary<ObjectGuid, ICharacter> { [player.Guid] = player }, [], new Dictionary<ObjectGuid, GameEntityFields>());
        Assert.True(player.CharacterGameState.Knows(targetGuid), "the target is not in the player's view");

        // Warm up: the first broadcast sends; the clock stands still, so every later one is throttled.
        for (int i = 0; i < 2; i++)
        {
            combat.Update(s_tick);
            broadcast.Tick(connections.Values, creatures, combat);
        }
        Assert.Equal(sent, connection.Sent);

        long before = ThreadAllocations.BeforeQuietWindow();
        for (int i = 0; i < 100; i++)
        {
            combat.Update(s_tick);
            broadcast.Tick(connections.Values, creatures, combat);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(sent, connection.Sent);
        Assert.Single(registry.Active);
    }
}
