using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #672: a creature's world state says it is dead once it has died. The kill reaches every watcher as a
/// change on the broadcast that carries its 0 health, and a client that first sees the corpse later is
/// told on the add. Through a real MapInstance and its combat service. Character ids 672_1xx, creature
/// ids 672_9xx.
/// </summary>
public class CreatureDeathReplicationShould
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    /// <summary>Takes each hit off health, as the combat scripts do.</summary>
    private sealed class CorpseTestWoundScript(Creature creature, ISimulationContext context) : AiScript(creature, context)
    {
        public override object State { get; set; } = 0;

        protected override bool ShouldRun() => false;

        public override void OnHit(IUnit attacker, uint damage) =>
            Creature.CurrentHealth = damage >= Creature.CurrentHealth ? 0u : Creature.CurrentHealth - damage;
    }

    [Fact]
    public void Tell_a_watcher_the_creature_is_dead_on_the_broadcast_that_carries_its_0_health()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient watcher = JoinAt(instance, 672_101, Vector3.zero);
        Creature boar = AddCreature(instance, 672_901, new Vector3(5f, 0f, 0f));
        Ticks(instance, 7);   // the add, and a broadcast past it
        Assert.Single(watcher.Added(), s => s.Guid == boar.Guid.RawValue);
        watcher.Sent.Clear();

        instance.CombatService.ApplyDamage(Killer(), boar, 1000);
        Ticks(instance, 7);   // at least one broadcast

        ObjectState update = watcher.StateUpdates().First(s => s.Guid == boar.Guid.RawValue);
        Assert.Equal(0u, update.CurrentHealth);
        Assert.True(update.IsDead);
    }

    [Fact]
    public void Add_a_corpse_as_dead_for_a_watcher_that_first_sees_it_after_the_kill()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient watcher = JoinAt(instance, 672_111, new Vector3(500f, 0f, 0f));
        Creature boar = AddCreature(instance, 672_911, new Vector3(5f, 0f, 0f));
        Ticks(instance, 2);
        instance.CombatService.ApplyDamage(Killer(), boar, 1000);
        Ticks(instance, 7);
        Assert.DoesNotContain(watcher.Added(), s => s.Guid == boar.Guid.RawValue);

        watcher.Character.Position = Vector3.zero;
        Ticks(instance, 1);

        ObjectState added = Assert.Single(watcher.Added(), s => s.Guid == boar.Guid.RawValue);
        Assert.Equal(0u, added.CurrentHealth);
        Assert.True(added.IsDead);
    }

    /// <summary>
    /// A hit that leaves the creature alive does not change its death state, so its update does not carry
    /// the member: the thousands of creature updates a second pay nothing for it.
    /// </summary>
    [Fact]
    public void Leave_the_death_state_out_of_a_living_creatures_update()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient watcher = JoinAt(instance, 672_131, Vector3.zero);
        Creature boar = AddCreature(instance, 672_931, new Vector3(5f, 0f, 0f));
        Ticks(instance, 7);
        watcher.Sent.Clear();

        instance.CombatService.ApplyDamage(Killer(), boar, 10);
        Ticks(instance, 7);

        ObjectState update = watcher.StateUpdates().First(s => s.Guid == boar.Guid.RawValue);
        Assert.Equal(90u, update.CurrentHealth);
        Assert.Null(update.IsDead);
    }

    /// <summary>Another creature, in no instance: a raw hit from it is a hit and nothing more.</summary>
    private static Creature Killer() => new()
    {
        Guid = new ObjectGuid(ObjectType.Creature, 672_999),
        Metadata = Loot.LootTestData.BoarTemplate(null),
        Position = new Vector3(6f, 0f, 0f),
        Health = 100,
        CurrentHealth = 100,
    };

    private static void Ticks(MapInstance instance, int count)
    {
        for (int i = 0; i < count; i++)
        {
            instance.Update(s_tick);
        }
    }

    private static MapInstanceClient JoinAt(MapInstance instance, uint id, Vector3 position)
    {
        MapInstanceClient client = Join(instance, id);
        client.Character.Position = position;
        return client;
    }

    private static Creature AddCreature(MapInstance instance, uint id, Vector3 position)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = position,
            Health = 100,
            CurrentHealth = 100,
        };
        creature.Script = new CorpseTestWoundScript(creature, instance);
        instance.AddCreature(creature);
        return creature;
    }
}
