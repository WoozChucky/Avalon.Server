using Avalon.Common;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// The combat half of a steady tick allocates nothing: the encounter update and the threat broadcast
/// run every tick of every instance, with an encounter live and a player targeting its creature.
/// </summary>
public class CombatTickAllocationShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);

    [Fact]
    public void Run_a_steady_combat_tick_without_allocating()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2001, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var config = new CombatConfig();
        var registry = new EncounterRegistry(config, clock);
        var combat = new CombatService(config, registry, time: clock);
        var broadcast = new ThreatBroadcastService(config, clock);

        var creature = new Creature
        {
            Guid          = new ObjectGuid(ObjectType.Creature, 9_001),
            Metadata      = Substitute.For<ICreatureMetadata>(),
            Health        = 100,
            CurrentHealth = 100,
        };
        CharacterEntity player = TestCharacters.New(9_002);
        combat.EnterCombat(creature, player);
        ((Encounter)registry.Active.Single()).AddThreat(creature, player, 1_000_000f);

        var connection = new QuietConnection(player) { CurrentTargetGuid = creature.Guid.RawValue };
        var connections = new Dictionary<ObjectGuid, IWorldConnection> { [player.Guid] = connection };
        var creatures = new Dictionary<ObjectGuid, ICreature> { [creature.Guid] = creature };

        // Warm up: the first broadcast sends; the clock stands still, so every later one is throttled.
        for (int i = 0; i < 2; i++)
        {
            combat.Update(Tick);
            broadcast.Tick(connections.Values, creatures, combat);
        }
        Assert.Equal(1, connection.Sent);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            combat.Update(Tick);
            broadcast.Tick(connections.Values, creatures, combat);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(1, connection.Sent);
        Assert.Single(registry.Active);
    }
}
