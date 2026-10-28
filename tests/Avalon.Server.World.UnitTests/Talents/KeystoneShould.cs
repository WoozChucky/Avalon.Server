using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World;
using Avalon.World.Abilities;
using Avalon.World.Auras;
using Avalon.World.Characters;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Talents;

/// <summary>
/// #1181 Part D: the three keystone rules, held through the seeded tree, through a real instance, the real cast handler
/// and the combat service. Blood Price (332) pays Cleave's 6 from life, never mana or the ward, the payment itself
/// tagging no combat (the cast enters combat as any cast does), and refuses a cost that would leave less than 1 life.
/// Steady Hand (334) keeps the crit chance at 0 whatever an aura grants, so no hit and no aura snapshot of its holder
/// crits, even on rolls of 0. Unbroken (335) lets the ward take blight and a bleed tick, and waits 8 s, not 4, before
/// the ward recharges. Character ids 118_401 upward.
/// </summary>
public class KeystoneShould
{
    private const int BloodPrice = 332, SteadyHand = 334, Unbroken = 335;

    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    [Theory]
    [InlineData("blood price pays life")]
    [InlineData("blood price pays no combat")]
    [InlineData("blood price at cost")]
    [InlineData("blood price at cost plus one")]
    [InlineData("steady hand never crits")]
    [InlineData("unbroken takes blight")]
    [InlineData("unbroken takes a bleed tick")]
    [InlineData("unbroken waits twice as long")]
    public void Bend_a_rule_by_its_keystone(string row)
    {
        StaticData data = ScenarioReferenceData.Shared.Data;
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        // Every chance roll 0: a crit chance above 0 would crit; nothing here has evasion or block to draw on it. Every
        // damage roll 14, the low end of Spark Bolt's spell base (14 to 20).
        ScriptedCombatRandom? random = row.StartsWith("steady", StringComparison.Ordinal)
            ? new ScriptedCombatRandom(new double[300]).Longs([.. Enumerable.Repeat(14L, 50)])
            : null;
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, world: NewWorld(data),
            random: random, time: time);

        if (row.StartsWith("blood", StringComparison.Ordinal))
            BloodPriceRow(instance, handler, data, time, row);
        else if (row.StartsWith("steady", StringComparison.Ordinal))
            SteadyHandRow(instance, handler, data, time);
        else
            UnbrokenRow(instance, data, time, row);
    }

    private static MapInstanceClient Enter(MapInstance instance, StaticData data, TimeProvider time, uint id,
        CharacterClass @class, params int[] held)
    {
        CharacterEntity character = Inventory.TestCharacters.New(id, time: time);
        character.Data!.Class = @class;
        MapInstanceClient client = Join(instance, character);
        TalentTestCharacters.Enter(character, data, held);
        character.PowerType = PowerType.Mana;   // as select sets it: every class casts from mana
        return client;
    }

    private static Creature Boar(MapInstance instance, uint id, Vector3 position,
        DamageType damageType = DamageType.Physical)
    {
        var boar = new Creature
        {
            DamageType = damageType,
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = position,
            Health = 100_000,
            CurrentHealth = 100_000,
        };
        boar.Script = new MapInstanceAbilityCastShould.WoundScript(boar, instance);
        instance.AddCreature(boar);
        return boar;
    }

    private static void BloodPriceRow(MapInstance instance, CastAbilityHandler handler, StaticData data,
        TimeProvider time, string row)
    {
        MapInstanceClient caster = Enter(instance, data, time, 118_401, CharacterClass.Warrior, 2, 3, 4, 5, 6, BloodPrice);
        CharacterEntity warrior = caster.Character;
        Creature? boar = row == "blood price pays no combat" ? null : Boar(instance, 118_901, new Vector3(0f, 0f, 1.2f));
        uint health = row switch
        {
            "blood price at cost" => 6u,
            "blood price at cost plus one" => 7u,
            _ => warrior.CurrentHealth,
        };
        warrior.CurrentHealth = health;
        (uint? mana, uint ward) = (warrior.CurrentPower, warrior.Ward);
        Assert.Equal(warrior.Power, mana);
        if (boar is null)
        {
            // Through the cast system, which pays: the handler tags every accepted cast in combat, whatever it costs.
            Assert.True(instance.RunInstantAbility(warrior, new AbilityAim(new Vector3(0f, 0f, 1f), null),
                warrior.Spells[new AbilityId(200)]!));
        }
        else
        {
            handler.Execute(caster.Connection,
                new CCastAbilityPacket { AbilityId = 200, GroundPos = new Vector3Dto { X = 0f, Y = 0f, Z = 5f } });
        }

        SAbilityNotReadyPacket? refused = caster.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY)
            .SingleOrDefault();
        if (row == "blood price at cost")
        {
            Assert.Equal(CastRejectReason.NotEnoughPower, refused?.Reason);
            Assert.Equal(6u, warrior.CurrentHealth);
            Assert.Equal(100_000u, boar!.CurrentHealth);
            return;
        }

        Assert.Null(refused);
        Assert.Equal(row == "blood price at cost plus one" ? 1u : health - 6u, warrior.CurrentHealth);
        Assert.Equal((mana, ward), (warrior.CurrentPower, warrior.Ward));
        if (boar is null)
            Assert.False(warrior.IsInCombat);
        else
            Assert.True(boar.CurrentHealth < 100_000u);
    }

    private static void SteadyHandRow(MapInstance instance, CastAbilityHandler handler, StaticData data, TimeProvider time)
    {
        MapInstanceClient caster = Enter(instance, data, time, 118_402, CharacterClass.Wizard, SteadyHand);
        CharacterEntity wizard = caster.Character;
        var keenEye = new AuraTemplate
        {
            Id = new AuraId(118_001),
            Name = "Keen Eye",
            Icon = "keen eye",
            Kind = AuraKind.Helpful,
            DurationMs = 60_000,
            Stacking = AuraStacking.Refresh,
            MaxStacks = 1,
            Modifiers = [new AuraStatModifier { AuraId = new AuraId(118_001), Stat = AuraStat.CritPct, Kind = AuraModifierKind.Flat, Value = 10f }],
        };
        Assert.Equal(AuraApplyResult.Applied, instance.Auras.Apply(null, wizard, keenEye, AuraSource.None));
        Assert.Equal(0f, wizard.Stats!.Value.CritPct);

        Creature boar = Boar(instance, 118_902, new Vector3(0f, 0f, 3f));
        handler.Execute(caster.Connection,
            new CCastAbilityPacket { AbilityId = 210, GroundPos = new Vector3Dto { X = 0f, Y = 0f, Z = 3f } });
        for (int i = 0; i < 40; i++)
            instance.Update(s_tick);

        SUnitDamagePacket spark = Assert.Single(caster.Read<SUnitDamagePacket>(NetworkPacketType.SMSG_CREATURE_DAMAGED));
        Assert.Equal(HitResult.None, spark.Result & HitResult.Crit);

        Assert.True(data.Abilities.TryGet(new AbilityId(213), out AbilityTemplate? ignite));
        var source = new AuraSource(ignite.Id, 1f, 0f, ignite.Affects, ignite.DamageType, AbilityMetadataMapper.From(ignite));
        Assert.Equal(AuraApplyResult.Applied, instance.Auras.Apply(wizard, boar, new AuraId(2), source));
        Assert.Equal(0f, Assert.Single(boar.Auras.All).Snapshot.CritPct);
    }

    private static void UnbrokenRow(MapInstance instance, StaticData data, FakeTimeProvider time, string row)
    {
        CharacterEntity cleric = Enter(instance, data, time, 118_403, CharacterClass.Cleric, Unbroken).Character;
        cleric.ApplyStats(new DerivedCharacterStats(MaxHealth: 500, MaxPower: 100, Stamina: 0, Strength: 0, Agility: 0,
            Intellect: 0, Armor: 0, BlockPct: 0f, Evasion: 0, CritPct: 0f, MaxWard: 30), CurrentValues.EnterWorld,
            TestCombat.Formula);
        Assert.Equal(30u, cleric.Ward);

        if (row == "unbroken takes a bleed tick")
        {
            double carry = 0d;
            var bleed = new PeriodicHit(null, cleric, new AuraId(1), 3f, new AuraSnapshot(0f, 0f, 1, DamageType.Physical),
                AuraSource.None);
            ((CombatService)instance.CombatService).ApplyPeriodicDamage(bleed, ref carry, lastTick: false);
            Assert.Equal((500u, 27u), (cleric.CurrentHealth, cleric.Ward));
            return;
        }

        // A creature of natural damage 8, its Rotting Burst at 150 %: 12 blight.
        Creature rotter = Boar(instance, 118_903, new Vector3(0f, 0f, 1f), DamageType.Blight);
        rotter.DamageMin = rotter.DamageMax = 8;
        Assert.True(data.Abilities.TryGet(new AbilityId(307), out AbilityTemplate? burst));
        IAbility rottingBurst = Substitute.For<IAbility>();
        rottingBurst.AbilityId.Returns(burst.Id);
        rottingBurst.Metadata.Returns(AbilityMetadataMapper.From(burst));

        Assert.Equal(HitOutcome.Landed,
            ((IAbilityArena)instance).HitForAbility(rotter, cleric, rottingBurst, HitShaping.Whole));
        Assert.Equal((500u, 18u), (cleric.CurrentHealth, cleric.Ward));
        if (row == "unbroken takes blight")
            return;

        time.Advance(TimeSpan.FromSeconds(5));
        instance.Update(s_tick);
        Assert.Equal(18u, cleric.Ward);

        time.Advance(TimeSpan.FromSeconds(4));
        instance.Update(s_tick);
        Assert.True(cleric.Ward > 18u);
    }
}
