using System.IO;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using NSubstitute;
using ProtoBuf;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>Casting through a real MapInstance. Character ids are unique to this class (164_1xx).</summary>
public class MapInstanceAbilityCastShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);

    /// <summary>Seven 60 Hz ticks: past the 0.1 s state broadcast interval.</summary>
    private static void TickUntilBroadcast(MapInstance instance)
    {
        for (int i = 0; i < 7; i++)
        {
            instance.Update(Tick);
        }
    }

    private static IAbility HealAbility()
    {
        var ability = Substitute.For<IAbility>();
        ability.AbilityId.Returns(new AbilityId(232));
        ability.Metadata.Returns(new AbilityMetadata { Name = "Heal", ScriptName = "x" });
        return ability;
    }

    private static IAbility DamageAbility(uint id)
    {
        var ability = Substitute.For<IAbility>();
        ability.AbilityId.Returns(new AbilityId(id));
        ability.Metadata.Returns(new AbilityMetadata { Name = "Strike", ScriptName = "x" });
        return ability;
    }

    /// <summary>The heal path (#164): a healed character's health rides the entity state update, to itself too.</summary>
    [Fact]
    public void Send_a_heals_health_change_in_the_next_state_update()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient healed = Join(instance, 164_111);
        MapInstanceClient watcher = Join(instance, 164_112);
        healed.Character.Health = 100;
        healed.Character.CurrentHealth = 40;
        TickUntilBroadcast(instance);
        healed.Sent.Clear();
        watcher.Sent.Clear();

        instance.CombatService.ApplyHeal(watcher.Character, healed.Character, 30, HealAbility());
        TickUntilBroadcast(instance);

        Assert.Contains(watcher.StateUpdates(), s => s.Guid == healed.Character.Guid.RawValue && s.CurrentHealth == 70u);
        Assert.Contains(healed.StateUpdates(), s => s.Guid == healed.Character.Guid.RawValue && s.CurrentHealth == 70u);
    }

    /// <summary>#521 item 8: the victim learns which ability hit it.</summary>
    [Fact]
    public void Name_the_ability_on_the_victims_damage_packet()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient victim = Join(instance, 164_121);
        victim.Character.Health = 100;
        victim.Character.CurrentHealth = 100;
        var attacker = Substitute.For<ICreature>();
        attacker.Guid.Returns(new ObjectGuid(ObjectType.Creature, 164_900u));

        instance.CombatService.ApplyDamage(attacker, victim.Character, 10, DamageAbility(id: 211));

        SCharacterDamagePacket damage = Assert.Single(victim.Read<SCharacterDamagePacket>(NetworkPacketType.SMSG_CHARACTER_DAMAGED));
        Assert.Equal(211u, damage.AbilityId);
        Assert.Equal(90u, damage.CurrentHealth);
    }

    /// <summary>A swing names no ability (#521 item 8).</summary>
    [Fact]
    public void Name_no_ability_on_a_swings_damage_packet()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient victim = Join(instance, 164_131);
        victim.Character.Health = 100;
        victim.Character.CurrentHealth = 100;
        var attacker = Substitute.For<ICreature>();
        attacker.Guid.Returns(new ObjectGuid(ObjectType.Creature, 164_901u));

        instance.CombatService.ApplyDamage(attacker, victim.Character, 10);

        SCharacterDamagePacket damage = Assert.Single(victim.Read<SCharacterDamagePacket>(NetworkPacketType.SMSG_CHARACTER_DAMAGED));
        Assert.Null(damage.AbilityId);
        Assert.Equal(90u, damage.CurrentHealth);
    }

    /// <summary>#521 item 9: other clients learn which ability a unit is casting, not just for how long.</summary>
    [Fact]
    public void Name_the_ability_on_the_start_cast_broadcast()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient caster = Join(instance, 164_101);
        MapInstanceClient watcher = Join(instance, 164_102);
        var ability = Substitute.For<IAbility>();
        ability.AbilityId.Returns(new AbilityId(211));
        ability.Metadata.Returns(new AbilityMetadata { Name = "Flame Burst", ScriptName = "x", CastTime = 0.6f });

        instance.BroadcastUnitStartCast(caster.Character, ability);

        foreach (MapInstanceClient client in new[] { caster, watcher })
        {
            SUnitStartCastPacket start = Assert.Single(client.Read<SUnitStartCastPacket>(NetworkPacketType.SMSG_UNIT_START_CAST));
            Assert.Equal(caster.Character.Guid.RawValue, start.Caster);
            Assert.Equal(211u, start.AbilityId);
            Assert.Equal(0.6f, start.CastTime);
        }
    }

    [Fact]
    public void Carry_the_ability_id_through_a_protobuf_round_trip()
    {
        var original = new SUnitStartCastPacket { Caster = 42UL, CastTime = 1.25f, AbilityId = 232 };
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, original);
        stream.Position = 0;

        SUnitStartCastPacket decoded = Serializer.Deserialize<SUnitStartCastPacket>(stream);

        Assert.Equal(232u, decoded.AbilityId);
        Assert.Equal(42UL, decoded.Caster);
        Assert.Equal(1.25f, decoded.CastTime);
    }
}
