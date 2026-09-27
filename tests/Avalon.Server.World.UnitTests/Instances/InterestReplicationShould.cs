using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #593: world-state replication through a real MapInstance is filtered per connection by the
/// interest range, 60 m to enter a client's view and 60 + 10 m to leave it, on X/Z. Character ids
/// (593_1xx) and creature ids (593_9xx) are unique to this class.
/// </summary>
public class InterestReplicationShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);

    [Fact]
    public void Send_no_add_for_a_creature_a_character_or_a_projectile_beyond_the_radius()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient watcher = JoinAt(instance, 593_101, Vector3.zero);
        Creature creature = AddCreature(instance, 593_901, new Vector3(100f, 0f, 0f));
        MapInstanceClient other = JoinAt(instance, 593_102, new Vector3(0f, 0f, 100f));
        CastProjectile(handler, other, towards: new Vector3(0f, 0f, 110f), reach: 10f);

        Ticks(instance, 30);

        Assert.DoesNotContain(watcher.Added(), s => s.Guid == creature.Guid.RawValue);
        Assert.DoesNotContain(watcher.Added(), s => s.Guid == other.Character.Guid.RawValue);
        Assert.DoesNotContain(watcher.Added(), s => IsProjectile(s.Guid));
        Assert.Contains(other.Added(), s => IsProjectile(s.Guid));   // the projectile did fly
        Assert.DoesNotContain(watcher.StateUpdates(), s => s.Guid != watcher.Character.Guid.RawValue);
        Assert.Empty(watcher.Removed());
    }

    [Fact]
    public void Add_a_creature_once_with_full_state_when_the_watcher_walks_up()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient watcher = JoinAt(instance, 593_111, Vector3.zero);
        Creature creature = AddCreature(instance, 593_911, new Vector3(100f, 0f, 0f));
        Ticks(instance, 2);
        Assert.DoesNotContain(watcher.Added(), s => s.Guid == creature.Guid.RawValue);

        watcher.Character.Position = new Vector3(50f, 0f, 0f);
        Ticks(instance, 2);

        ObjectState added = Assert.Single(watcher.Added(), s => s.Guid == creature.Guid.RawValue);
        Assert.Equal(100u, added.Health);
        Assert.Equal(100u, added.CurrentHealth);
        Assert.Equal(100f, added.Position!.X);
        Assert.NotNull(added.Velocity);
        Assert.NotNull(added.MoveState);
        Assert.NotNull(added.Level);
    }

    [Fact]
    public void Remove_a_creature_once_when_the_watcher_walks_away()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient watcher = JoinAt(instance, 593_121, Vector3.zero);
        Creature creature = AddCreature(instance, 593_921, new Vector3(10f, 0f, 0f));
        Ticks(instance, 2);
        Assert.Single(watcher.Added(), s => s.Guid == creature.Guid.RawValue);

        watcher.Character.Position = new Vector3(200f, 0f, 0f);
        Ticks(instance, 2);

        Assert.Equal([creature.Guid.RawValue], watcher.Removed());
    }

    [Fact]
    public void Not_churn_between_the_radius_and_the_margin()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient watcher = JoinAt(instance, 593_131, Vector3.zero);
        Creature creature = AddCreature(instance, 593_931, new Vector3(30f, 0f, 0f));
        Ticks(instance, 2);

        foreach (float distance in new[] { 61f, 69f, 62f, 70f, 61f })
        {
            watcher.Character.Position = new Vector3(30f - distance, 0f, 0f);
            Ticks(instance, 1);
        }

        Assert.Single(watcher.Added(), s => s.Guid == creature.Guid.RawValue);
        Assert.Empty(watcher.Removed());
    }

    /// <summary>Review focus 4: a removal, then a re-add on the next tick, carries what changed while away.</summary>
    [Fact]
    public void Re_add_with_the_state_that_changed_while_away()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient watcher = JoinAt(instance, 593_141, Vector3.zero);
        Creature creature = AddCreature(instance, 593_941, new Vector3(50f, 0f, 0f));
        Ticks(instance, 2);

        watcher.Character.Position = new Vector3(-30f, 0f, 0f);   // 80 m away: removed
        Ticks(instance, 1);
        Assert.Equal([creature.Guid.RawValue], watcher.Removed());
        creature.CurrentHealth = 37;
        Ticks(instance, 7);   // past a broadcast, so the change's dirty mark is consumed while away

        watcher.Character.Position = new Vector3(0f, 0f, 0f);
        Ticks(instance, 1);

        List<ObjectState> adds = watcher.Added().Where(s => s.Guid == creature.Guid.RawValue).ToList();
        Assert.Equal(2, adds.Count);
        Assert.Equal(37u, adds[1].CurrentHealth);
        Assert.Equal(100u, adds[1].Health);
        Assert.Equal(50f, adds[1].Position!.X);
    }

    [Fact]
    public void Show_two_watchers_different_objects_on_the_same_tick()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient a = JoinAt(instance, 593_151, Vector3.zero);
        MapInstanceClient b = JoinAt(instance, 593_152, new Vector3(500f, 0f, 0f));
        Creature nearA = AddCreature(instance, 593_951, new Vector3(10f, 0f, 0f));
        Creature nearB = AddCreature(instance, 593_952, new Vector3(510f, 0f, 0f));

        instance.Update(Tick);

        Assert.Equal(
            new[] { a.Character.Guid.RawValue, nearA.Guid.RawValue }.Order(),
            a.Added().Select(s => s.Guid).Order());
        Assert.Equal(
            new[] { b.Character.Guid.RawValue, nearB.Guid.RawValue }.Order(),
            b.Added().Select(s => s.Guid).Order());
    }

    [Fact]
    public void Never_remove_the_watchers_own_character()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient watcher = JoinAt(instance, 593_161, Vector3.zero);
        Ticks(instance, 2);

        watcher.Character.Position = new Vector3(1e5f, 0f, 0f);
        Ticks(instance, 2);
        watcher.Character.Position = new Vector3(float.NaN, 0f, 0f);
        Ticks(instance, 2);

        Assert.Single(watcher.Added(), s => s.Guid == watcher.Character.Guid.RawValue);
        Assert.DoesNotContain(watcher.Character.Guid.RawValue, watcher.Removed());
    }

    /// <summary>Review focus 2: a corpse's client keeps its surroundings, seen from where it lies.</summary>
    [Fact]
    public void Keep_seeing_by_position_while_dead()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient watcher = JoinAt(instance, 593_171, Vector3.zero);
        watcher.Character.CurrentHealth = 0;
        watcher.Character.IsDead = true;
        Creature near = AddCreature(instance, 593_971, new Vector3(30f, 0f, 0f));
        Creature far = AddCreature(instance, 593_972, new Vector3(0f, 0f, 100f));

        Ticks(instance, 2);

        Assert.Single(watcher.Added(), s => s.Guid == near.Guid.RawValue);
        Assert.DoesNotContain(watcher.Added(), s => s.Guid == far.Guid.RawValue);
    }

    /// <summary>
    /// Review focus 3: a projectile cast from out of range enters the view mid-flight with full state,
    /// then its final update and its remove still arrive (#164); a watcher farther away gets none of them.
    /// </summary>
    [Fact]
    public void Add_a_projectile_that_flies_into_range()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient caster = JoinAt(instance, 593_181, Vector3.zero);
        MapInstanceClient watcher = JoinAt(instance, 593_182, new Vector3(0f, 0f, 100f));   // 100 m ahead
        MapInstanceClient farther = JoinAt(instance, 593_183, new Vector3(500f, 0f, 0f));
        Ticks(instance, 1);
        watcher.Sent.Clear();
        farther.Sent.Clear();

        CastProjectile(handler, caster, towards: new Vector3(0f, 0f, 90f), reach: 90f);
        Ticks(instance, 20);   // 20 m/s for a third of a second: still beyond 60 m of the watcher
        Assert.DoesNotContain(watcher.Added(), s => IsProjectile(s.Guid));
        Ticks(instance, 400);   // well past the 4.5 s flight

        List<string> seen = Sequence(watcher);
        Assert.Equal("add", seen[0]);
        Assert.Equal(1, seen.Count(k => k == "add"));
        Assert.Equal("remove", seen[^1]);
        Assert.Equal(1, seen.Count(k => k == "remove"));
        Assert.Equal("update", seen[^2]);
        ObjectState added = Assert.Single(watcher.Added(), s => IsProjectile(s.Guid));
        Assert.NotNull(added.Position);
        Assert.InRange(added.Position!.Z, 40f, 41f);   // entered at 60 m from the watcher, mid-flight
        Assert.NotNull(added.Velocity);
        ObjectState final = watcher.StateUpdates().Last(s => IsProjectile(s.Guid));
        Assert.Equal(0f, final.Velocity!.Z);
        Assert.Empty(Sequence(farther));
    }

    [Fact]
    public void Still_send_the_threat_list_for_a_target_out_of_view()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient watcher = JoinAt(instance, 593_191, Vector3.zero);
        Creature creature = AddCreature(instance, 593_991, new Vector3(1f, 0f, 0f));
        creature.Script = new MapInstanceAbilityCastShould.WoundScript(creature);
        instance.CombatService.ApplyDamage(watcher.Character, creature, 10);
        Ticks(instance, 2);
        Assert.Contains(watcher.Added(), s => s.Guid == creature.Guid.RawValue);
        watcher.Character.Position = new Vector3(500f, 0f, 0f);
        Ticks(instance, 2);
        Assert.Contains(creature.Guid.RawValue, watcher.Removed());

        // Targeted only now, from out of view: the first threat list for a new target is always sent.
        watcher.Connection.CurrentTargetGuid.Returns(creature.Guid.RawValue);
        Ticks(instance, 1);

        Assert.NotEmpty(watcher.Read<SThreatListPacket>(NetworkPacketType.SMSG_THREAT_LIST));
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private static void Ticks(MapInstance instance, int count)
    {
        for (int i = 0; i < count; i++)
        {
            instance.Update(Tick);
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
        instance.AddCreature(creature);
        return creature;
    }

    /// <summary>Casts a 20 m/s projectile from <paramref name="caster" /> toward <paramref name="towards" />.</summary>
    private static void CastProjectile(CastAbilityHandler handler, MapInstanceClient caster, Vector3 towards, float reach)
    {
        caster.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.Projectile(210, reach: reach, speed: 20f))]);
        handler.Execute(caster.Connection, new CCastAbilityPacket
        {
            AbilityId = 210,
            GroundPos = new Vector3Dto { X = towards.x, Y = towards.y, Z = towards.z },
        });
        Assert.Empty(caster.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY));
    }

    private static bool IsProjectile(ulong guid) => new ObjectGuid(guid).Type == ObjectType.SpellProjectile;

    /// <summary>The adds, updates and removes of projectiles this client was sent, in order.</summary>
    private static List<string> Sequence(MapInstanceClient client)
    {
        List<string> seen = [];
        foreach (NetworkPacket packet in client.Sent)
        {
            var single = new MapInstanceClient(client.Connection, client.Character, [packet]);
            seen.AddRange(single.Added().Where(s => IsProjectile(s.Guid)).Select(_ => "add"));
            seen.AddRange(single.StateUpdates().Where(s => IsProjectile(s.Guid)).Select(_ => "update"));
            seen.AddRange(single.Removed().Where(IsProjectile).Select(_ => "remove"));
        }

        return seen;
    }
}
