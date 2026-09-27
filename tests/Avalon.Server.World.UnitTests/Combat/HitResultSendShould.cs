using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// #506: the damage packets say whether a hit was a crit, dodged or blocked, and a dodge, which deals
/// nothing, is still sent, with 0. Through the real MapInstance and CombatService, with a scripted
/// combat random. Character ids 506_3xx, creature ids 506_8xx.
/// </summary>
public class HitResultSendShould
{
    private static readonly Vector3 Far = new(500f, 0f, 500f);

    /// <summary>Takes each hit off health and reports it to its instance, as the combat script does.</summary>
    private sealed class BroadcastingWoundScript(Creature creature, ISimulationContext context) : AiScript(creature, context)
    {
        public int Hits { get; private set; }

        public override object State { get; set; } = 0;

        protected override bool ShouldRun() => false;

        public override void OnHit(IUnit attacker, uint damage)
        {
            Hits++;
            Creature.CurrentHealth = damage >= Creature.CurrentHealth ? 0u : Creature.CurrentHealth - damage;
            if (Creature.CurrentHealth > 0)
                Context.BroadcastUnitHit(attacker, Creature, Creature.CurrentHealth, damage);
        }
    }

    private static Creature AddCreature(MapInstance instance, uint id, Vector3 position, float dodge = 0f,
        float block = 0f)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = position,
            Health = 100,
            CurrentHealth = 100,
            DodgePct = dodge,
            BlockPct = block,
            Level = 1,
        };
        creature.Script = new BroadcastingWoundScript(creature, instance);
        instance.AddCreature(creature);
        return creature;
    }

    private static MapInstanceClient At(MapInstance instance, uint id, Vector3 position)
    {
        MapInstanceClient client = Join(instance, id);
        client.Character.Position = position;
        client.Character.Health = 100;
        client.Character.CurrentHealth = 100;
        return client;
    }

    /// <summary>A character that dodges, blocks and crits at 30 %, 50 % and 50 %.</summary>
    private static void GiveDefences(CharacterEntity character) =>
        character.ApplyStats(new DerivedCharacterStats(MaxHealth: 100, MaxPower: 100, Stamina: 0, Strength: 0, Agility: 0,
            Intellect: 0, Armor: 0, BlockPct: 50f, DodgePct: 30f, CritPct: 50f, AttackDamage: 0, AbilityDamage: 0),
            CurrentValues.Refill);

    private static List<SUnitDamagePacket> UnitHits(MapInstanceClient client) =>
        client.Read<SUnitDamagePacket>(NetworkPacketType.SMSG_CREATURE_DAMAGED);

    [Fact]
    public void Send_a_dodge_on_a_creature_with_0_and_dodged_to_the_watchers_in_range_only()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: new ScriptedCombatRandom(0.0));
        MapInstanceClient attacker = At(instance, 506_301, Vector3.zero);
        MapInstanceClient near = At(instance, 506_302, new Vector3(5f, 0f, 0f));
        MapInstanceClient far = At(instance, 506_303, Far);
        Creature boar = AddCreature(instance, 506_801, new Vector3(1f, 0f, 0f), dodge: 30f);

        instance.CombatService.ApplyDamage(attacker.Character, boar, 10);

        SUnitDamagePacket seen = Assert.Single(UnitHits(near));
        Assert.Equal((boar.Guid.RawValue, 0u, 100u, HitResult.Dodged), (seen.Target, seen.Damage, seen.CurrentHealth, seen.Result));
        Assert.Single(UnitHits(attacker));
        Assert.Empty(far.Sent);
        Assert.Equal(0, ((BroadcastingWoundScript)boar.Script!).Hits);
        Assert.NotNull(instance.CombatService.GetEncounterFor(boar));
    }

    [Fact]
    public void Send_a_dodge_on_a_character_to_it_with_0_and_dodged_and_to_its_watchers()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: new ScriptedCombatRandom(0.0));
        MapInstanceClient victim = At(instance, 506_311, Vector3.zero);
        GiveDefences(victim.Character);
        MapInstanceClient watcher = At(instance, 506_312, new Vector3(3f, 0f, 0f));
        Creature boar = AddCreature(instance, 506_811, new Vector3(1f, 0f, 0f));

        instance.CombatService.ApplyDamage(boar, victim.Character, 10);

        SCharacterDamagePacket own = Assert.Single(victim.Read<SCharacterDamagePacket>(NetworkPacketType.SMSG_CHARACTER_DAMAGED));
        Assert.Equal((0u, 100u, HitResult.Dodged), (own.Damage, own.CurrentHealth, own.Result));
        Assert.Null(own.AbilityId);
        SUnitDamagePacket seen = Assert.Single(UnitHits(watcher));
        Assert.Equal((0u, HitResult.Dodged), (seen.Damage, seen.Result));
        Assert.Equal(100u, victim.Character.CurrentHealth);
    }

    [Fact]
    public void Mark_a_crit_on_a_creature_crit()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: new ScriptedCombatRandom(0.99, 0.0, 0.99));
        MapInstanceClient attacker = At(instance, 506_321, Vector3.zero);
        GiveDefences(attacker.Character);
        Creature boar = AddCreature(instance, 506_821, new Vector3(1f, 0f, 0f));

        instance.CombatService.ApplyDamage(attacker.Character, boar, 10);

        SUnitDamagePacket seen = Assert.Single(UnitHits(attacker));
        Assert.Equal((15u, HitResult.Crit), (seen.Damage, seen.Result));
    }

    [Fact]
    public void Mark_a_block_on_a_creature_block()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: new ScriptedCombatRandom(0.99, 0.99, 0.0));
        MapInstanceClient attacker = At(instance, 506_331, Vector3.zero);
        Creature boar = AddCreature(instance, 506_831, new Vector3(1f, 0f, 0f), block: 10f);

        instance.CombatService.ApplyDamage(attacker.Character, boar, 10);

        SUnitDamagePacket seen = Assert.Single(UnitHits(attacker));
        Assert.Equal((5u, HitResult.Blocked), (seen.Damage, seen.Result));
    }

    [Fact]
    public void Mark_a_crit_and_a_block_on_a_characters_own_packet_and_its_broadcast()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: new ScriptedCombatRandom(0.99, 0.0, 0.0));
        MapInstanceClient attacker = At(instance, 506_341, new Vector3(1f, 0f, 0f));
        GiveDefences(attacker.Character);
        MapInstanceClient victim = At(instance, 506_342, Vector3.zero);
        GiveDefences(victim.Character);
        attacker.Character.Data!.PvpEnabled = true;
        victim.Character.Data!.PvpEnabled = true;

        instance.CombatService.ApplyDamage(attacker.Character, victim.Character, 20,
            AbilityTestData.Game(AbilityTestData.Circle(201, radius: 3f)));

        SCharacterDamagePacket own = Assert.Single(victim.Read<SCharacterDamagePacket>(NetworkPacketType.SMSG_CHARACTER_DAMAGED));
        Assert.Equal((15u, HitResult.Crit | HitResult.Blocked, (uint?)201u), (own.Damage, own.Result, own.AbilityId));
        Assert.Equal(HitResult.Crit | HitResult.Blocked, Assert.Single(UnitHits(attacker)).Result);
    }

    [Fact]
    public void Say_none_on_a_plain_hit_and_clear_the_result_once_the_hit_is_over()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: new ScriptedCombatRandom(0.99, 0.0, 0.99));
        MapInstanceClient attacker = At(instance, 506_351, Vector3.zero);
        GiveDefences(attacker.Character);
        Creature boar = AddCreature(instance, 506_851, new Vector3(1f, 0f, 0f));
        instance.CombatService.ApplyDamage(attacker.Character, boar, 10);   // a crit
        attacker.Sent.Clear();

        instance.BroadcastUnitHit(attacker.Character, boar, 50, 5);   // a broadcast outside any hit

        Assert.Equal(HitResult.None, Assert.Single(UnitHits(attacker)).Result);
    }

    /// <summary>#506 review: a killing crit is sent as a hit, marked Crit, and then the death.</summary>
    [Fact]
    public async Task Send_a_killing_crit_as_a_crit_hit_before_the_death()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld(await TestStaticData.LoadAsync()), random: new ScriptedCombatRandom(0.99, 0.0, 0.99));
        MapInstanceClient attacker = At(instance, 506_371, Vector3.zero);
        GiveDefences(attacker.Character);

        var boar = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 506_871),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = new Vector3(1f, 0f, 0f),
            Health = 100,
            CurrentHealth = 10,
            Level = 1,
        };
        boar.Script = new Avalon.World.Scripts.Creatures.CreatureCombatScript(
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, boar, instance);
        instance.AddCreature(boar);

        instance.CombatService.ApplyDamage(attacker.Character, boar, 10);

        SUnitDamagePacket hit = Assert.Single(UnitHits(attacker));
        Assert.Equal((10u, 0u, HitResult.Crit), (hit.Damage, hit.CurrentHealth, hit.Result));
        Assert.Equal([NetworkPacketType.SMSG_CREATURE_DAMAGED, NetworkPacketType.SMSG_UNIT_DEATH],
            attacker.Sent.Select(p => p.Header.Type)
                .Where(t => t is NetworkPacketType.SMSG_CREATURE_DAMAGED or NetworkPacketType.SMSG_UNIT_DEATH));
    }

    /// <summary>#506 review: a script whose OnHit throws leaves no result behind for the next hit's broadcast.</summary>
    [Fact]
    public void Leave_no_stale_result_after_an_on_hit_that_throws()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: new ScriptedCombatRandom(0.99, 0.0, 0.99, 0.99, 0.99, 0.99));
        MapInstanceClient attacker = At(instance, 506_381, Vector3.zero);
        GiveDefences(attacker.Character);
        Creature boar = AddCreature(instance, 506_881, new Vector3(1f, 0f, 0f));
        AiScript wound = boar.Script!;
        boar.Script = new ThrowingScript(boar, instance);

        Assert.Throws<InvalidOperationException>(() => instance.CombatService.ApplyDamage(attacker.Character, boar, 10));   // a crit
        boar.Script = wound;
        instance.CombatService.ApplyDamage(attacker.Character, boar, 10);   // plain

        Assert.Equal(HitResult.None, Assert.Single(UnitHits(attacker)).Result);
    }

    private sealed class ThrowingScript(Creature creature, ISimulationContext context) : AiScript(creature, context)
    {
        public override object State { get; set; } = 0;

        protected override bool ShouldRun() => false;

        public override void OnHit(IUnit attacker, uint damage) => throw new InvalidOperationException("script bug");
    }

    /// <summary>Review focus 2: one cleave on three units, the middle one dodging.</summary>
    [Fact]
    public void Send_two_hits_and_one_dodge_for_a_cleave_on_three_units_one_of_which_dodges()
    {
        var rng = new ScriptedCombatRandom(0.99, 0.99, 0.99, 0.0, 0.99, 0.99, 0.99);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, random: rng);
        MapInstanceClient warrior = At(instance, 506_361, Vector3.zero);
        warrior.Character.PowerType = PowerType.Fury;
        warrior.Character.Power = 100;
        warrior.Character.CurrentPower = 0;
        warrior.Character.Orientation = new Vector3(0f, 0f, 0f);
        AbilityTemplate cleave = AbilityTestData.Cone(200, reach: 2.5f, arc: 100f);
        cleave.PowerGainPerHit = 8;
        warrior.Character.Spells.Load([AbilityTestData.Game(cleave)]);
        AddCreature(instance, 506_861, new Vector3(0f, 0f, 1.0f), dodge: 30f);
        AddCreature(instance, 506_862, new Vector3(0f, 0f, 1.6f), dodge: 30f);
        AddCreature(instance, 506_863, new Vector3(0f, 0f, 2.2f), dodge: 30f);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        List<SUnitDamagePacket> hits = UnitHits(warrior);
        Assert.Equal([HitResult.None, HitResult.Dodged, HitResult.None], hits.Select(h => h.Result));
        Assert.Equal([10u, 0u, 10u], hits.Select(h => h.Damage));
        Assert.Equal(16u, warrior.Character.CurrentPower);
    }
}
