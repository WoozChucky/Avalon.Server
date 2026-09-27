using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World.Characters;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// #506 slice C: a heal that restored health is sent as SMSG_UNIT_HEALED, with what it restored, the
/// target's health after it and whether it crit, to the healer, the target and whoever stands within the
/// interest radius of the target (#532). Through the real MapInstance and CombatService, with a scripted
/// combat random. Character ids 506_5xx.
/// </summary>
public class HealSendShould
{
    private static readonly Vector3 Far = new(500f, 0f, 500f);

    private static IAbility Heal()
    {
        var ability = Substitute.For<IAbility>();
        ability.AbilityId.Returns(new AbilityId(232));
        ability.Metadata.Returns(new AbilityMetadata { Name = "Mending Circle", ScriptName = "x" });
        return ability;
    }

    private static MapInstanceClient At(MapInstance instance, uint id, Vector3 position, uint current = 100)
    {
        MapInstanceClient client = Join(instance, id);
        client.Character.Position = position;
        client.Character.Health = 100;
        client.Character.CurrentHealth = current;
        return client;
    }

    private static void GiveCrit(CharacterEntity character)
    {
        character.ApplyStats(new DerivedCharacterStats(MaxHealth: 100, MaxPower: 100, Stamina: 0, Strength: 0, Agility: 0,
            Intellect: 0, Armor: 0, BlockPct: 0f, DodgePct: 0f, CritPct: 50f, AttackDamage: 0, AbilityDamage: 0),
            CurrentValues.Refill);
    }

    private static List<SUnitHealedPacket> Heals(MapInstanceClient client) =>
        client.Read<SUnitHealedPacket>(NetworkPacketType.SMSG_UNIT_HEALED);

    [Fact]
    public void Send_the_restored_amount_and_the_current_health()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: ScriptedCombatRandom.Plain());
        MapInstanceClient healer = At(instance, 506_501, Vector3.zero);
        MapInstanceClient target = At(instance, 506_502, new Vector3(2f, 0f, 0f), current: 40);

        instance.CombatService.ApplyHeal(healer.Character, target.Character, 25, Heal());

        SUnitHealedPacket heal = Assert.Single(Heals(target));
        Assert.Equal((healer.Character.Guid.RawValue, target.Character.Guid.RawValue, 25u, 65u, (uint?)232u, HitResult.None),
            (heal.Healer, heal.Target, heal.Amount, heal.CurrentHealth, heal.AbilityId, heal.Result));
        Assert.Single(Heals(healer));
    }

    [Fact]
    public void Send_only_the_health_restored_when_the_heal_overheals()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: ScriptedCombatRandom.Plain());
        MapInstanceClient healer = At(instance, 506_511, Vector3.zero);
        MapInstanceClient target = At(instance, 506_512, new Vector3(2f, 0f, 0f), current: 90);

        instance.CombatService.ApplyHeal(healer.Character, target.Character, 25, Heal());

        SUnitHealedPacket heal = Assert.Single(Heals(target));
        Assert.Equal((10u, 100u), (heal.Amount, heal.CurrentHealth));
    }

    [Fact]
    public void Mark_a_critical_heal_crit()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: new ScriptedCombatRandom(0.0));
        MapInstanceClient healer = At(instance, 506_521, Vector3.zero);
        GiveCrit(healer.Character);
        MapInstanceClient target = At(instance, 506_522, new Vector3(2f, 0f, 0f), current: 40);

        instance.CombatService.ApplyHeal(healer.Character, target.Character, 25, Heal());

        SUnitHealedPacket heal = Assert.Single(Heals(target));
        Assert.Equal((37u, 77u, HitResult.Crit), (heal.Amount, heal.CurrentHealth, heal.Result));
    }

    [Fact]
    public void Send_nothing_for_a_target_at_full_health()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: ScriptedCombatRandom.Plain());
        MapInstanceClient healer = At(instance, 506_531, Vector3.zero);
        MapInstanceClient target = At(instance, 506_532, new Vector3(2f, 0f, 0f));

        instance.CombatService.ApplyHeal(healer.Character, target.Character, 25, Heal());

        Assert.Empty(Heals(target));
        Assert.Empty(Heals(healer));
    }

    [Fact]
    public void Send_it_to_a_watcher_near_the_target_and_not_to_one_far_from_it()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: ScriptedCombatRandom.Plain());
        MapInstanceClient healer = At(instance, 506_541, Vector3.zero);
        MapInstanceClient target = At(instance, 506_542, new Vector3(2f, 0f, 0f), current: 40);
        MapInstanceClient near = At(instance, 506_543, new Vector3(10f, 0f, 0f));
        MapInstanceClient far = At(instance, 506_544, Far);

        instance.CombatService.ApplyHeal(healer.Character, target.Character, 25, Heal());

        Assert.Single(Heals(near));
        Assert.Empty(far.Sent);
    }

    [Fact]
    public void Send_it_to_the_healer_and_the_target_however_far_apart()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: ScriptedCombatRandom.Plain());
        MapInstanceClient target = At(instance, 506_551, Vector3.zero, current: 40);
        MapInstanceClient healer = At(instance, 506_552, Far);
        // Beside the healer and far from the target: the heal's point is the target.
        MapInstanceClient besideHealer = At(instance, 506_553, Far + new Vector3(1f, 0f, 0f));

        instance.CombatService.ApplyHeal(healer.Character, target.Character, 25, Heal());

        Assert.Single(Heals(healer));
        Assert.Single(Heals(target));
        Assert.Empty(Heals(besideHealer));
    }
}
