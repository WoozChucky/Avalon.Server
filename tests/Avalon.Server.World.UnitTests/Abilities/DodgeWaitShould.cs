using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World.Abilities;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>
/// The dodge wait (#1082): a telegraph's hit on a player is judged by that player's screen. When the cast fires, each
/// player in its footprint is hit only after their one-way delay (a 120 ms round trip: 4 ticks), and only if they are
/// still in it then; the wait can only remove a hit, never add one. Character ids 1082_5xx, creature ids 1082_95x.
/// </summary>
public class DodgeWaitShould
{
    private const uint NovaId = 315;
    private const long Start = 1_000;
    private const int Wait = 4;

    private static readonly Vector3 s_inside = new(0f, 0f, 2f);
    private static readonly Vector3 s_outside = new(0f, 0f, 10f);

    public enum During
    {
        Stays,
        StepsOut,
        StepsIn,
        CasterDies,
    }

    /// <summary>
    /// A creature's 3 m telegraphed circle fires on a player. The player's action in the wait decides: staying in takes the
    /// hit once, on the due tick, never on the fire tick; stepping out before it is a dodge; standing outside at the fire
    /// and stepping in is never a hit. A caster that dies in the wait still lands it: the cast already fired.
    /// </summary>
    [Theory]
    [InlineData(During.Stays, Wait)]
    [InlineData(During.StepsOut, 0)]
    [InlineData(During.StepsIn, 0)]
    [InlineData(During.CasterDies, Wait)]
    public void Hit_a_player_only_if_still_inside_when_the_wait_ends(During action, int hitOnTickAfterFire)
    {
        var ticks = new WorldTicks(Start);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler _, worldTicks: ticks);
        MapInstanceClient player = Player(instance, 1082_501, action == During.StepsIn ? s_outside : s_inside);
        (Creature caster, _) = AddCreature(instance, 1082_951, Vector3.zero);

        FireNova(instance, ticks, caster);
        Assert.Equal(100u, player.Character.CurrentHealth);

        int hitOn = 0;
        for (int tick = 1; tick <= Wait + 4; tick++)
        {
            if (tick == 1 && action == During.StepsIn)
                player.Character.Position = s_inside;
            if (tick == 1 && action == During.CasterDies)
                caster.CurrentHealth = 0;
            if (tick == Wait - 1 && action == During.StepsOut)
                player.Character.Position = s_outside;

            Tick(instance, ticks);
            if (hitOn == 0 && player.Character.CurrentHealth < 100u)
                hitOn = tick;
        }

        Assert.Equal(hitOnTickAfterFire, hitOn);
        Assert.Equal(hitOnTickAfterFire > 0 ? 1 : 0,
            player.Read<SCharacterDamagePacket>(NetworkPacketType.SMSG_CHARACTER_DAMAGED).Count);
    }

    /// <summary>Only players wait: a creature in a player's telegraphed circle is hit on the tick it fires.</summary>
    [Fact]
    public void Hit_a_creature_target_on_the_fire_tick()
    {
        var ticks = new WorldTicks(Start);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, worldTicks: ticks);
        MapInstanceClient caster = Player(instance, 1082_511, Vector3.zero);
        GameAbility nova = AbilityTestData.Game(Nova());
        caster.Character.Spells.Load([nova]);
        (_, CombatResolutionShould.CountingWoundScript wounds) = AddCreature(instance, 1082_961, s_inside);

        handler.Execute(caster.Connection, new CCastAbilityPacket { AbilityId = NovaId });
        Assert.True(nova.Casting);
        RunToFire(instance, ticks, nova);

        Assert.Equal(1, wounds.Hits);
    }

    /// <summary>
    /// Only hostile casts wait: a player's telegraphed heal circle heals an ally on the tick it fires, so stepping out in
    /// what would have been the wait loses nothing.
    /// </summary>
    [Fact]
    public void Heal_an_ally_on_the_fire_tick()
    {
        var ticks = new WorldTicks(Start);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, worldTicks: ticks);
        MapInstanceClient healer = Player(instance, 1082_531, Vector3.zero);
        AbilityTemplate row = Nova();
        row.Affects = AbilityAffects.Ally;
        row.Effects = SpellEffect.Heal;
        row.EffectValue = 40;
        GameAbility heal = AbilityTestData.Game(row);
        healer.Character.Spells.Load([heal]);
        MapInstanceClient ally = Player(instance, 1082_532, s_inside);
        ally.Character.CurrentHealth = 50;

        handler.Execute(healer.Connection, new CCastAbilityPacket { AbilityId = NovaId });
        Assert.True(heal.Casting);
        RunToFire(instance, ticks, heal);
        uint healed = ally.Character.CurrentHealth;
        ally.Character.Position = s_outside;
        for (int tick = 0; tick < Wait + 4; tick++)
            Tick(instance, ticks);

        Assert.True(healed > 50u, $"the ally has {healed} health on the fire tick");
        Assert.Equal(healed, ally.Character.CurrentHealth);
    }

    /// <summary>A player who leaves the instance in the wait takes nothing, and the pending hit goes with them.</summary>
    [Fact]
    public void Drop_the_hit_on_a_player_who_leaves_during_the_wait()
    {
        var ticks = new WorldTicks(Start);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler _, worldTicks: ticks);
        MapInstanceClient player = Player(instance, 1082_521, s_inside);
        (Creature caster, _) = AddCreature(instance, 1082_971, Vector3.zero);

        FireNova(instance, ticks, caster);
        Tick(instance, ticks);
        instance.RemoveCharacter(player.Connection);
        for (int tick = 0; tick < Wait + 4; tick++)
            Tick(instance, ticks);

        Assert.Equal(100u, player.Character.CurrentHealth);
        Assert.Empty(player.Read<SCharacterDamagePacket>(NetworkPacketType.SMSG_CHARACTER_DAMAGED));
    }

    /// <summary>Bramble Nova's shape: a 3 m circle on the caster, after a 100 ms wind-up.</summary>
    private static AbilityTemplate Nova()
    {
        AbilityTemplate row = AbilityTestData.Circle(NovaId, radius: 3f);
        row.CastTime = 100;
        return row;
    }

    /// <summary>Queues the creature's nova and ticks until it fires: the fire tick is the last one run.</summary>
    private static void FireNova(MapInstance instance, WorldTicks ticks, Creature caster)
    {
        GameAbility nova = AbilityTestData.Game(Nova());
        Assert.True(instance.QueueAbility(caster, new AbilityAim(new Vector3(0f, 0f, 1f), null), nova));
        RunToFire(instance, ticks, nova);
    }

    private static void RunToFire(MapInstance instance, WorldTicks ticks, GameAbility nova)
    {
        for (int i = 0; i < 60 && nova.Casting; i++)
            Tick(instance, ticks);
        Assert.False(nova.Casting);
    }

    private static void Tick(MapInstance instance, WorldTicks ticks)
    {
        instance.Update(TimeSpan.FromSeconds(1d / 60d));
        ticks.Advance();
    }

    /// <summary>A player at full health whose connection reports a 120 ms round trip: a 4-tick wait.</summary>
    private static MapInstanceClient Player(MapInstance instance, uint id, Vector3 position)
    {
        MapInstanceClient player = Join(instance, id);
        player.Character.Position = position;
        player.Character.Orientation = Vector3.zero;
        player.Character.Health = 100;
        player.Character.CurrentHealth = 100;
        player.Connection.RoundTripTime.Returns(TimeSpan.FromMilliseconds(120).Ticks);
        return player;
    }

    private static (Creature, CombatResolutionShould.CountingWoundScript) AddCreature(MapInstance instance, uint id,
        Vector3 position)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = position,
            Health = 100,
            CurrentHealth = 100,
            Level = 1,
        };
        var wounds = new CombatResolutionShould.CountingWoundScript(creature);
        creature.Script = wounds;
        instance.AddCreature(creature);
        return (creature, wounds);
    }
}
