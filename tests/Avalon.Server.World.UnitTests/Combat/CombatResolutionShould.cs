using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Auras;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Reload;
using Avalon.World.Scripts.Creatures;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// #506, #1178: every hit resolves through the attacker's weapon, offence and ability and the defender's armour,
/// evasion and block, with crits, through the real MapInstance, cast handler, shape scripts and CombatService, with a
/// scripted combat random. A seeded level-1 Warrior (Strength 23) has 80.5 % increased physical attack damage, so its
/// Cleave (315 % effectiveness) deals a weapon roll × 3.15 × 1.805. Character ids 506_1xx, creature ids 506_9xx.
/// </summary>
public class CombatResolutionShould
{
    private static readonly ClassLevelStat s_warriorLevel1 = new()
    {
        Class = CharacterClass.Warrior,
        Level = 1,
        BaseHp = 130,
        BaseMana = 0,
        Stamina = 22,
        Strength = 23,
        Agility = 20,
        Intellect = 20,
    };

    private static readonly ClassLevelStat s_warriorLevel2 = new()
    {
        Class = CharacterClass.Warrior,
        Level = 2,
        BaseHp = 160,
        BaseMana = 0,
        Stamina = 24,
        Strength = 25,
        Agility = 21,
        Intellect = 20,
    };

    private static readonly ClassLevelStat s_wizardLevel1 = new()
    {
        Class = CharacterClass.Wizard,
        Level = 1,
        BaseHp = 16,
        BaseMana = 20,
        Stamina = 21,
        Strength = 20,
        Agility = 20,
        Intellect = 23,
    };

    private static readonly ClassLevelStat[] s_rows = [s_warriorLevel1, s_warriorLevel2, s_wizardLevel1];

    /// <summary>A starter-range sword with no stats of its own, so Strength stays 23.</summary>
    private static readonly ItemTemplate s_sword = Weapon(506_001);

    private static readonly ItemTemplate s_bigSword = Weapon(506_002);

    private static readonly ItemTemplate s_staff = Weapon(506_003);

    // The damage each weapon's copy rolled (#1179): a weapon's damage is its copy's base stats.
    private static readonly Dictionary<ItemTemplateId, ItemBaseStats> s_rolled = new()
    {
        [s_sword.Id] = new ItemBaseStats { DamageMin = 4, DamageMax = 7 },
        [s_bigSword.Id] = new ItemBaseStats { DamageMin = 20, DamageMax = 20 },
        [s_staff.Id] = new ItemBaseStats { DamageMin = 4, DamageMax = 7 },
    };

    /// <summary>Chest armour 24, the plan's worked example.</summary>
    private static readonly ItemTemplate s_plate = new()
    {
        Id = new ItemTemplateId(506_004),
        Name = "Plate",
        Slot = ItemSlotType.Chest,
        MaxStackSize = 1,
        StatType1 = StatType.Armor,
        StatValue1 = 24,
    };

    private static readonly Dictionary<ItemTemplateId, ItemTemplate> s_templates =
        new[] { s_sword, s_bigSword, s_staff, s_plate }.ToDictionary(t => t.Id);

    private static ItemTemplate Weapon(ulong id) => new()
    {
        Id = new ItemTemplateId(id),
        Name = $"Weapon {id}",
        Class = ItemClass.Weapon,
        SubClass = ItemSubClass.OneHanded,
        Slot = ItemSlotType.MainHand,
        MaxStackSize = 1,
    };

    /// <summary>Cleave as seeded (#1178): an Attack at 315 % of a weapon roll, 8 Fury per unit damaged.</summary>
    private static AbilityTemplate Cleave()
    {
        AbilityTemplate cleave = AbilityTestData.Cone(200, reach: 2.5f, arc: 100f);
        cleave.PowerGainPerHit = 8;
        cleave.Tags = AbilityTags.Attack | AbilityTags.Melee | AbilityTags.Area;
        cleave.DamageEffectiveness = 315f;
        return cleave;
    }

    /// <summary>Spark Bolt's spell base on a cone (#1178): 14..20 lightning, no weapon roll.</summary>
    private static AbilityTemplate SparkCone()
    {
        AbilityTemplate bolt = AbilityTestData.Cone(210, reach: 6f, arc: 100f);
        bolt.Tags = AbilityTags.Spell | AbilityTags.Area;
        bolt.DamageType = DamageType.Lightning;
        bolt.SpellBaseMin = 14;
        bolt.SpellBaseMax = 20;
        bolt.SpellBasePerLevelPct = 0.7f;
        return bolt;
    }

    private static MapInstanceClient Warrior(MapInstance instance, uint id, ItemTemplate? weapon = null,
        params AbilityTemplate[] abilities)
    {
        MapInstanceClient warrior = Join(instance, New(id));
        warrior.Character.PowerType = PowerType.Fury;
        warrior.Character.Orientation = new Vector3(0f, 0f, 0f);
        if (weapon is not null)
            Wield(warrior.Character, weapon);
        Assert.True(CharacterStatsRefresh.Apply(warrior.Character, s_rows, TestCombat.Factors, Find, CurrentValues.Refill, TestCombat.Formula));
        warrior.Character.Spells.Load(abilities.Select(AbilityTestData.Game).ToArray());
        return warrior;
    }

    private static ItemTemplate? Find(ItemTemplateId id) => s_templates.GetValueOrDefault(id);

    /// <summary>A copy of <paramref name="weapon" /> in the main hand, carrying the base stats its copy rolled.</summary>
    private static void Wield(CharacterEntity character, ItemTemplate weapon)
    {
        InventoryItem item = Item(EquipmentSlots.MainHand, weapon);
        List<ItemInstanceBaseStat> rows = [];
        foreach ((ItemBaseStat stat, int value) in s_rolled[weapon.Id])
            rows.Add(new ItemInstanceBaseStat { ItemInstanceId = item.InstanceId, Stat = stat, Value = value });

        character.ItemModifiers.Load([new ItemInstance { Id = item.InstanceId, TemplateId = weapon.Id, BaseStats = rows }]);
        character.Container(InventoryType.Equipment).Load([item]);
    }

    private static Creature AddCreature(MapInstance instance, uint id, Vector3 position, uint health = 100,
        uint armor = 0, uint evasion = 0, float block = 0f, ushort level = 1, Resistances resistances = default,
        bool broadcast = false)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = position,
            Health = health,
            CurrentHealth = health,
            Armor = armor,
            Evasion = evasion,
            BlockPct = block,
            Level = level,
            Resistances = resistances,
        };
        creature.Script = new CountingWoundScript(creature, broadcast ? instance : null);
        instance.AddCreature(creature);
        return creature;
    }

    /// <summary>
    /// Takes each hit off health, as the combat script does, and counts them; given a context, it also broadcasts each
    /// hit it survives there, as the combat script does.
    /// </summary>
    internal sealed class CountingWoundScript(Creature creature, ISimulationContext? broadcastTo = null)
        : AiScript(creature, broadcastTo ?? Substitute.For<ISimulationContext>())
    {
        public int Hits { get; private set; }

        public override object State { get; set; } = 0;

        public override TimeSpan NextUpdateIn => TimeSpan.Zero;

        protected override bool ShouldRun() => false;

        public override void OnHit(IUnit attacker, uint damage)
        {
            Hits++;
            Creature.CurrentHealth = damage >= Creature.CurrentHealth ? 0u : Creature.CurrentHealth - damage;
            if (broadcastTo is not null && Creature.CurrentHealth > 0)
                broadcastTo.BroadcastUnitHit(attacker, Creature, Creature.CurrentHealth, damage);
        }
    }

    /// <summary>The threat every player starts an encounter's list with.</summary>
    private static readonly float s_seed = new Avalon.World.Public.Combat.CombatConfig().InitialThreatSeed;

    private static float ThreatOf(MapInstance instance, Creature creature, IUnit attacker) =>
        instance.CombatService.GetEncounterFor(creature)!.GetThreatList(creature)[attacker];

    /// <summary>A character's offence with the given increased points, for a stats refresh written by hand.</summary>
    private static OffenceStats Offence(params (OffenceStat Stat, float Points)[] increased)
    {
        OffenceStats offence = default;
        foreach ((OffenceStat stat, float points) in increased) offence.AddIncreased(stat, points);
        return offence;
    }

    /// <summary>Level-1 stats with nothing but <paramref name="weapon" /> and <paramref name="offence" />.</summary>
    private static DerivedCharacterStats Attacking(WeaponDamage weapon, OffenceStats offence) =>
        new(MaxHealth: 240, MaxPower: 100, Stamina: 0, Strength: 0, Agility: 0, Intellect: 0, Armor: 0, BlockPct: 0f,
            Evasion: 0, CritPct: 0f, Weapon: weapon, Offence: offence);

    private static readonly WeaponDamage s_tenToTen = new(10, 10, DamageType.Physical, 0, 0, null, 800);

    // ---- the base: weapon, spell base and offence ----

    [Fact]
    public void Deal_cleave_with_a_starter_sword_from_the_weapon_roll_and_strength()
    {
        ScriptedCombatRandom rng = ScriptedCombatRandom.Plain().Longs(5);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_101, s_sword, Cleave());
        Creature target = AddCreature(instance, 506_901, new Vector3(0f, 0f, 2f));

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        Assert.Equal(80.5f, warrior.Character.Stats!.Value.Offence.Increased(OffenceStat.PhysicalAttack));   // 23 x 3.5
        Assert.Equal(100u - 28u, target.CurrentHealth);   // floor(5 x 3.15 x 1.805)
        Assert.Equal([(4L, 7L)], rng.WeaponRolls);
    }

    /// <summary>#1178 (review focus 1): with no weapon, an attack rolls the formula's bare hand, 3..6.</summary>
    [Fact]
    public void Deal_cleave_unarmed_from_the_bare_hands_roll()
    {
        ScriptedCombatRandom rng = ScriptedCombatRandom.Plain().Longs(3);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_111, weapon: null, Cleave());
        Creature target = AddCreature(instance, 506_911, new Vector3(0f, 0f, 2f));

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        Assert.Equal(100u - 17u, target.CurrentHealth);   // floor(3 x 3.15 x 1.805)
        Assert.Equal([(3L, 6L)], rng.WeaponRolls);
    }

    /// <summary>
    /// #1178: an Attack converts half its weapon roll to cold before scaling, then each part is scaled and mitigated on
    /// its own: physical 10 x 50 % x 200 % x 1.735 (Strength 21's 73.5 %) = 17.35 against no armour, cold 10 x 50 % x
    /// 200 % = 10 against 50 % cold resistance = 5. The hit is both types, and physical, its largest part, names it.
    /// </summary>
    [Fact]
    public void Hit_with_the_weapon_roll_converted_scaled_and_typed()
    {
        ScriptedCombatRandom rng = new ScriptedCombatRandom().Longs(10).Doubles(0.99, 0.99, 0.99);
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: rng);
        MapInstanceClient hunter = Join(instance, New(506_191));
        hunter.Character.ApplyStats(Attacking(s_tenToTen, Offence((OffenceStat.PhysicalAttack, 73.5f))),
            CurrentValues.Refill, TestCombat.Formula);
        Creature target = AddCreature(instance, 506_991, new Vector3(1f, 0f, 0f),
            resistances: new Resistances(0f, 50f, 0f, 0f), broadcast: true);
        AbilityTemplate shot = AbilityTestData.Projectile(223);
        shot.DamageEffectiveness = 200f;
        shot.ConvertToType = DamageType.Cold;
        shot.ConvertPct = 50f;

        instance.CombatService.ApplyDamage(hunter.Character, target, AbilityTestData.Game(shot));

        Assert.Equal(100u - 22u, target.CurrentHealth);   // floor(17.35 + 5)
        SUnitDamagePacket hit = Assert.Single(hunter.Read<SUnitDamagePacket>(NetworkPacketType.SMSG_CREATURE_DAMAGED));
        Assert.Equal((DamageTypeMask.Physical | DamageTypeMask.Cold, DamageType.Physical), (hit.Types, hit.Type));
        Assert.Equal((0, 0), (rng.LongsLeft, rng.DoublesLeft));
    }

    [Fact]
    public void Scale_a_spell_by_its_base_and_intellect_and_never_roll_the_weapon()
    {
        ScriptedCombatRandom rng = ScriptedCombatRandom.Plain().Longs(14);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, random: rng);
        var row = new Avalon.Domain.Characters.Character
        {
            Id = new CharacterId(506_121),
            AccountId = new AccountId(1),
            Name = "Wizard",
            Class = CharacterClass.Wizard,
            CreationDate = DateTime.UtcNow,
        };
        var wizard = new CharacterEntity(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, row,
            new Avalon.Combat.RegenConfiguration())
        { Data = row };
        MapInstanceClient caster = Join(instance, wizard);
        wizard.PowerType = PowerType.Mana;
        Wield(wizard, s_staff);
        Assert.True(CharacterStatsRefresh.Apply(wizard, s_rows, TestCombat.Factors, Find, CurrentValues.Refill, TestCombat.Formula));
        wizard.Spells.Load([AbilityTestData.Game(SparkCone())]);
        Creature target = AddCreature(instance, 506_921, new Vector3(0f, 0f, 2f));

        handler.Execute(caster.Connection, new CCastAbilityPacket { AbilityId = 210 });

        Assert.Equal(100u - 25u, target.CurrentHealth);   // floor(14 x (1 + 23 x 3.5 %))
        Assert.Equal([(14L, 20L)], rng.WeaponRolls);      // the spell base's roll alone
    }

    // ---- armour, both ways ----

    [Fact]
    public void Mitigate_a_creatures_swing_by_the_players_armour()
    {
        var rng = ScriptedCombatRandom.Plain();
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: rng);
        MapInstanceClient warrior = Join(instance, New(506_131));
        warrior.Character.Container(InventoryType.Equipment).Load([Item(EquipmentSlots.Chest, s_plate)]);
        Assert.True(CharacterStatsRefresh.Apply(warrior.Character, s_rows, TestCombat.Factors, Find, CurrentValues.Refill, TestCombat.Formula));
        Creature boar = AddCreature(instance, 506_931, new Vector3(1f, 0f, 0f), level: 3);
        uint before = warrior.Character.CurrentHealth;

        instance.CombatService.ApplyDamage(boar, warrior.Character, 10);

        // floor(10 x (1 - 24 / (24 + 50 + 10 x 3))) = floor(7.69), of which the ward from Intellect 20 takes 5 (#1178)
        Assert.Equal(before - 2u, warrior.Character.CurrentHealth);
    }

    [Fact]
    public void Mitigate_a_players_hit_by_the_creatures_armour_and_count_threat_from_what_landed()
    {
        ScriptedCombatRandom rng = ScriptedCombatRandom.Plain().Longs(3);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_141, weapon: null, Cleave());
        Creature target = AddCreature(instance, 506_941, new Vector3(0f, 0f, 2f), armor: 60);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        // 17.06 x (1 - 60 / (60 + 50 + 10 x 1)) = 8.5, floored
        Assert.Equal(100u - 8u, target.CurrentHealth);
        Assert.Equal(s_seed + 8f * 2.0f, ThreatOf(instance, target, warrior.Character));   // Warrior threat x2
    }

    // ---- dodge and crit ----

    [Fact]
    public void Deal_nothing_on_a_dodge_but_still_add_threat_for_the_base_and_form_the_encounter()
    {
        ScriptedCombatRandom rng = new ScriptedCombatRandom(0.0).Longs(3);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_151, weapon: null, Cleave());
        Creature target = AddCreature(instance, 506_951, new Vector3(0f, 0f, 2f), evasion: 10000);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        Assert.Equal(100u, target.CurrentHealth);
        Assert.Equal(0, ((CountingWoundScript)target.Script!).Hits);
        Assert.NotNull(instance.CombatService.GetEncounterFor(target));
        // The base, 3 x 3.15 x 1.805 = 17.06, at the Warrior's threat x2.
        Assert.Equal(s_seed + 17.05725f * 2.0f, ThreatOf(instance, target, warrior.Character), precision: 3);
        Assert.Equal(0u, warrior.Character.CurrentPower);   // no Fury from a dodged hit
        Assert.True(warrior.Character.IsInCombat);
    }

    [Fact]
    public void Multiply_a_crit_by_the_crit_multiplier()
    {
        ScriptedCombatRandom rng = new ScriptedCombatRandom(0.99, 0.0, 0.99).Longs(3);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_161, weapon: null, Cleave());
        Creature target = AddCreature(instance, 506_961, new Vector3(0f, 0f, 2f));

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        Assert.Equal(100u - 25u, target.CurrentHealth);   // floor(17.06 x 1.5)
    }

    /// <summary>
    /// #1178: a heal is its spell base (10..10) times its increased healing alone (50 %); the healer's 100 % increased
    /// spell damage counts for nothing, and the ally's armour takes none of it.
    /// </summary>
    [Fact]
    public void Heal_from_the_base_range_with_increased_healing_alone()
    {
        ScriptedCombatRandom rng = ScriptedCombatRandom.Plain().Longs(10);
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: rng);
        MapInstanceClient healer = Join(instance, New(506_165));
        healer.Character.ApplyStats(Attacking(default, Offence((OffenceStat.Healing, 50f), (OffenceStat.Spell, 100f))),
            CurrentValues.Refill, TestCombat.Formula);
        MapInstanceClient ally = Join(instance, New(506_166));
        ally.Character.Container(InventoryType.Equipment).Load([Item(EquipmentSlots.Chest, s_plate)]);
        Assert.True(CharacterStatsRefresh.Apply(ally.Character, s_rows, TestCombat.Factors, Find, CurrentValues.Refill, TestCombat.Formula));
        ally.Character.CurrentHealth = 10;
        AbilityTemplate mending = AbilityTestData.HealCircle(232);
        mending.SpellBaseMin = 10;
        mending.SpellBaseMax = 10;

        instance.CombatService.ApplyHeal(healer.Character, ally.Character, AbilityTestData.Game(mending));

        Assert.Equal(10u + 15u, ally.Character.CurrentHealth);
        Assert.Equal((1, 0), (rng.DoublesDrawn, rng.LongsLeft));
    }

    /// <summary>
    /// #1178 (review focus 4): a damage over time's total is rolled from its caster once, when it lands: Rend's Bleed
    /// (PeriodicBase 12, 50 % of its source's roll) from a 10..10 weapon at 100 % increased physical attack damage is
    /// (12 + 5) x 2 = 34 over 4 ticks, 8.5 each. The caster then drops its weapon and dies before the second tick, and
    /// every tick is still 8.5, 34 in all.
    /// </summary>
    [Fact]
    public void Keep_a_damage_over_time_total_from_the_moment_it_was_applied()
    {
        var h = new AuraHarness(random: ScriptedCombatRandom.Plain(1000).Longs(10));
        h.Use(AuraTestData.Bleed());
        CharacterEntity warrior = h.Player(506_195);
        warrior.ApplyStats(Attacking(s_tenToTen, Offence((OffenceStat.PhysicalAttack, 100f))), CurrentValues.Refill,
            TestCombat.Formula);
        Creature boar = h.Creature(506_995, health: 1000);
        AbilityTemplate rend = AbilityTestData.Cone(203);
        rend.AuraId = new AuraId(901);

        h.Auras.Apply(warrior, boar, new AuraId(901), Avalon.World.Auras.AuraSource.Of(AbilityTestData.Game(rend)));
        h.Advance(TimeSpan.FromSeconds(3));
        h.Auras.Update();
        warrior.ApplyStats(Attacking(WeaponDamage.Of(null, TestCombat.Formula), default), CurrentValues.KeepShare,
            TestCombat.Formula);
        warrior.IsDead = true;

        var ticks = new List<float> { Assert.Single(boar.Auras.All).Snapshot.PerTickPerStack };
        for (int tick = 1; tick < 4; tick++)
        {
            h.Advance(TimeSpan.FromSeconds(3));
            if (boar.Auras.All.Count == 1) ticks.Add(boar.Auras.All.Single().Snapshot.PerTickPerStack);
            h.Auras.Update();
        }

        Assert.Equal([8.5f, 8.5f, 8.5f, 8.5f], ticks);
        Assert.Equal(1000u - 34u, boar.CurrentHealth);
    }

    // ---- what reads the resolved damage ----

    [Fact]
    public void Gain_fury_from_the_health_the_mitigated_hit_took()
    {
        var rng = ScriptedCombatRandom.Plain();
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: rng);
        MapInstanceClient warrior = Join(instance, New(506_171));
        warrior.Character.PowerType = PowerType.Fury;
        warrior.Character.Container(InventoryType.Equipment).Load([Item(EquipmentSlots.Chest, s_plate)]);
        Assert.True(CharacterStatsRefresh.Apply(warrior.Character, s_rows, TestCombat.Factors, Find, CurrentValues.Refill, TestCombat.Formula));
        warrior.Character.CurrentPower = 0;
        Creature boar = AddCreature(instance, 506_971, new Vector3(1f, 0f, 0f), level: 1);
        uint before = warrior.Character.CurrentHealth;

        instance.CombatService.ApplyDamage(boar, warrior.Character, 100);

        // Armour 24 and 2 from Strength (#1178): floor(100 x (1 - 26 / 86)) = 69, of which the ward from Intellect 20
        // takes 5: 64 lost of 240, and Fury from those
        Assert.Equal(before - 64u, warrior.Character.CurrentHealth);
        Assert.Equal(Fury.FromDamageTaken(64, before, warrior.Character.Health, 50f), warrior.Character.CurrentPower);
    }

    [Fact]
    public void Draw_no_roll_for_an_invulnerable_target_a_corpse_a_returning_creature_or_a_dead_heal_target()
    {
        var rng = new ScriptedCombatRandom();
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_181, s_sword);
        IAbility cleave = AbilityTestData.Game(Cleave());
        Creature npc = AddCreature(instance, 506_981, new Vector3(0f, 0f, 2f));
        npc.Invulnerable = true;
        Creature corpse = AddCreature(instance, 506_982, new Vector3(0.6f, 0f, 1.5f));
        corpse.CurrentHealth = 0;
        Creature returning = CombatServiceShould.CreatureReturningHome(
            Substitute.For<ISimulationContext>(), nameof(CreatureCombatScript), health: 30);
        instance.AddCreature(returning);
        MapInstanceClient dead = Join(instance, New(506_182));
        dead.Character.IsDead = true;

        instance.CombatService.ApplyDamage(warrior.Character, npc, cleave);
        instance.CombatService.ApplyDamage(warrior.Character, corpse, cleave);
        instance.CombatService.ApplyDamage(warrior.Character, returning, cleave);
        instance.CombatService.ApplyHeal(warrior.Character, dead.Character, AbilityTestData.Game(AbilityTestData.HealCircle(232)));

        Assert.Equal(0, rng.DoublesDrawn);
        Assert.Empty(rng.WeaponRolls);
    }

    // ---- review focus 1: mid-fight changes ----

    [Fact]
    public void Use_a_better_weapon_on_the_very_next_hit_after_equipping_it()
    {
        ScriptedCombatRandom rng = ScriptedCombatRandom.Plain().Longs(5, 20);
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_191, s_sword);
        IAbility cleave = AbilityTestData.Game(Cleave());
        Creature target = AddCreature(instance, 506_991, new Vector3(0f, 0f, 2f), health: 1000);

        instance.CombatService.ApplyDamage(warrior.Character, target, cleave);
        Assert.Equal(1000u - 28u, target.CurrentHealth);   // floor(5 x 3.15 x 1.805)

        Wield(warrior.Character, s_bigSword);
        Assert.True(CharacterStatsRefresh.Apply(warrior.Character, s_rows, TestCombat.Factors, Find, CurrentValues.KeepShare, TestCombat.Formula));
        instance.CombatService.ApplyDamage(warrior.Character, target, cleave);

        Assert.Equal(1000u - 28u - 113u, target.CurrentHealth);   // floor(20 x 3.15 x 1.805)
        Assert.Equal([(4L, 7L), (20L, 20L)], rng.WeaponRolls);
    }

    [Fact]
    public void Use_the_new_levels_stats_on_the_next_hit_after_a_level_up()
    {
        ScriptedCombatRandom rng = ScriptedCombatRandom.Plain().Longs(3, 3);
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_192);
        IAbility cleave = AbilityTestData.Game(Cleave());
        Creature target = AddCreature(instance, 506_992, new Vector3(0f, 0f, 2f), health: 1000, armor: 60);

        instance.CombatService.ApplyDamage(warrior.Character, target, cleave);
        Assert.Equal(1000u - 8u, target.CurrentHealth);   // the bare hand's 3: 17.06 x (1 - 60 / 120)

        warrior.Character.Level = 2;
        Assert.True(CharacterStatsRefresh.Apply(warrior.Character, s_rows, TestCombat.Factors, Find, CurrentValues.Refill, TestCombat.Formula));
        instance.CombatService.ApplyDamage(warrior.Character, target, cleave);

        // Strength 25 now (87.5 %), and armour weighs less against level 2: 3 x 3.15 x 1.875 x (1 - 60 / 130) = 9.5
        Assert.Equal(1000u - 8u - 9u, target.CurrentHealth);
    }

    // ---- review focus 2: one cast, several units ----

    [Fact]
    public void Roll_each_unit_of_a_cleave_on_its_own_and_gain_only_for_the_ones_damaged()
    {
        // Nearest first: the first unit lands, the second dodges, the third lands. Each draws its bare-hand roll first.
        ScriptedCombatRandom rng = new ScriptedCombatRandom(0.99, 0.99, 0.99, 0.0, 0.99, 0.99, 0.99).Longs(3, 3, 3);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_201, weapon: null, Cleave());
        warrior.Character.CurrentPower = 0;
        Creature[] targets =
        [
            AddCreature(instance, 506_901_1, new Vector3(0f, 0f, 1.0f), evasion: 10000),
            AddCreature(instance, 506_901_2, new Vector3(0f, 0f, 1.6f), evasion: 10000),
            AddCreature(instance, 506_901_3, new Vector3(0f, 0f, 2.2f), evasion: 10000),
        ];

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        Assert.Equal([83u, 100u, 83u], targets.Select(t => t.CurrentHealth));   // floor(3 x 3.15 x 1.805) = 17
        Assert.Equal(16u, warrior.Character.CurrentPower);
        Assert.Equal(0, rng.DoublesLeft);
    }

    // ---- review focus 5: a reload between two hits of one cast ----

    [Fact]
    public async Task Resolve_each_hit_with_the_one_formula_it_read_even_when_a_reload_lands_mid_cast()
    {
        CombatFormula heavy = CombatSeed.Formula();
        heavy.ArmorBase = 1000f;
        var formulas = new List<CombatFormula> { CombatSeed.Formula() };
        ICombatDataRepository repository = Substitute.For<ICombatDataRepository>();
        repository.GetFormulasAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyCollection<CombatFormula>>(formulas.ToList()));
        repository.GetClassStatFactorsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<ClassStatFactors>>(CombatSeed.ClassFactors()));
        StaticData data = await TestStaticData.LoadAsync(TestStaticData.Repositories(combat: repository));
        formulas[0] = heavy;
        var next = (CombatPatch)await data.PrepareAsync(ReloadArea.Combat);

        // The new generation is applied after the first unit's crit roll, in the middle of its resolve.
        var rng = new ReloadingRandom(afterDraws: 2, () => data.Apply(next));
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, world: NewWorld(data), random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_211, weapon: null, Cleave());
        Creature first = AddCreature(instance, 506_921_1, new Vector3(0f, 0f, 1.0f), armor: 60);
        Creature second = AddCreature(instance, 506_921_2, new Vector3(0f, 0f, 2.0f), armor: 60);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        // The bare hand's lowest roll, 3: 17.06 before armour.
        Assert.Equal(100u - 8u, first.CurrentHealth);     // the seeded formula: 17.06 x (1 - 60 / 120)
        Assert.Equal(100u - 16u, second.CurrentHealth);   // the reloaded one: 17.06 x (1 - 60 / 1070)
    }

    /// <summary>Every chance roll misses; after <c>afterDraws</c> of them it runs <c>reload</c> once.</summary>
    private sealed class ReloadingRandom(int afterDraws, Action reload) : ICombatRandom
    {
        private int _drawn;

        public double NextDouble()
        {
            if (++_drawn == afterDraws) reload();
            return 0.99;
        }

        public long NextInt64(long minInclusive, long maxInclusive) => minInclusive;
    }
}
