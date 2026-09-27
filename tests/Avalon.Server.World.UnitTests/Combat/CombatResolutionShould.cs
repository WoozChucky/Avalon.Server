using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Inventory;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Reload;
using Avalon.World.Scripts.Creatures;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// #506: every hit resolves through the attacker's stats, weapon and skill and the defender's armour,
/// dodge and block, with crits, through the real MapInstance, cast handler, shape scripts and
/// CombatService, with a scripted combat random. A seeded level-1 Warrior attacks for 46. Character ids
/// 506_1xx, creature ids 506_9xx.
/// </summary>
public class CombatResolutionShould
{
    private static readonly ClassLevelStat WarriorLevel1 = new()
    {
        Class = CharacterClass.Warrior, Level = 1, BaseHp = 20, BaseMana = 0,
        Stamina = 22, Strength = 23, Agility = 20, Intellect = 20,
    };

    private static readonly ClassLevelStat WarriorLevel2 = new()
    {
        Class = CharacterClass.Warrior, Level = 2, BaseHp = 40, BaseMana = 0,
        Stamina = 24, Strength = 25, Agility = 21, Intellect = 20,
    };

    private static readonly ClassLevelStat WizardLevel1 = new()
    {
        Class = CharacterClass.Wizard, Level = 1, BaseHp = 16, BaseMana = 20,
        Stamina = 21, Strength = 20, Agility = 20, Intellect = 23,
    };

    private static readonly ClassLevelStat[] Rows = [WarriorLevel1, WarriorLevel2, WizardLevel1];

    /// <summary>A starter-range sword with no stats of its own, so the attack stays 46.</summary>
    private static readonly ItemTemplate Sword = Weapon(506_001, 4, 7);

    private static readonly ItemTemplate BigSword = Weapon(506_002, 20, 20);

    private static readonly ItemTemplate Staff = Weapon(506_003, 4, 7);

    /// <summary>Chest armour 24, the plan's worked example.</summary>
    private static readonly ItemTemplate Plate = new()
    {
        Id = new ItemTemplateId(506_004), Name = "Plate", Slot = ItemSlotType.Chest, MaxStackSize = 1,
        StatType1 = StatType.Armor, StatValue1 = 24,
    };

    private static readonly Dictionary<ItemTemplateId, ItemTemplate> Templates =
        new[] { Sword, BigSword, Staff, Plate }.ToDictionary(t => t.Id);

    private static ItemTemplate Weapon(ulong id, uint min, uint max) => new()
    {
        Id = new ItemTemplateId(id), Name = $"Weapon {id}", Class = ItemClass.Weapon, SubClass = ItemSubClass.OneHanded,
        Slot = ItemSlotType.MainHand, MaxStackSize = 1, DamageMin1 = min, DamageMax1 = max,
    };

    /// <summary>Cleave as seeded (#506): 12 + 0.3 x AttackDamage + 1.0 x a weapon roll, 8 Fury per unit damaged.</summary>
    private static AbilityTemplate Cleave()
    {
        AbilityTemplate cleave = AbilityTestData.Cone(200, reach: 2.5f, arc: 100f);
        cleave.EffectValue = 12;
        cleave.PowerGainPerHit = 8;
        cleave.ScalingStat = ScalingStat.Attack;
        cleave.ScalingCoefficient = 0.3f;
        cleave.WeaponCoefficient = 1.0f;
        return cleave;
    }

    /// <summary>Arcane Bolt's scaling on a cone (#506): 12 + 0.25 x AbilityDamage, no weapon term.</summary>
    private static AbilityTemplate ArcaneCone()
    {
        AbilityTemplate bolt = AbilityTestData.Cone(210, reach: 6f, arc: 100f);
        bolt.EffectValue = 12;
        bolt.ScalingStat = ScalingStat.Ability;
        bolt.ScalingCoefficient = 0.25f;
        return bolt;
    }

    private static MapInstanceClient Warrior(MapInstance instance, uint id, ItemTemplate? weapon = null,
        params AbilityTemplate[] abilities)
    {
        MapInstanceClient warrior = Join(instance, New(id));
        warrior.Character.PowerType = PowerType.Fury;
        warrior.Character.Orientation = new Vector3(0f, 0f, 0f);
        if (weapon is not null)
            warrior.Character.Container(InventoryType.Equipment).Load([Item(EquipmentSlots.MainHand, weapon)]);
        Assert.True(CharacterStatsRefresh.Apply(warrior.Character, Rows, TestCombat.Factors, Find, CurrentValues.Refill));
        warrior.Character.Spells.Load(abilities.Select(AbilityTestData.Game).ToArray());
        return warrior;
    }

    private static ItemTemplate? Find(ItemTemplateId id) => Templates.GetValueOrDefault(id);

    private static Creature AddCreature(MapInstance instance, uint id, Vector3 position, uint health = 100,
        uint armor = 0, float dodge = 0f, float block = 0f, ushort level = 1)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = position,
            Health = health,
            CurrentHealth = health,
            Armor = armor,
            DodgePct = dodge,
            BlockPct = block,
            Level = level,
        };
        creature.Script = new CountingWoundScript(creature);
        instance.AddCreature(creature);
        return creature;
    }

    /// <summary>Takes each hit off health, as the combat script does, and counts them.</summary>
    internal sealed class CountingWoundScript(Creature creature) : AiScript(creature, Substitute.For<ISimulationContext>())
    {
        public int Hits { get; private set; }

        public override object State { get; set; } = 0;

        protected override bool ShouldRun() => false;

        public override void OnHit(IUnit attacker, uint damage)
        {
            Hits++;
            Creature.CurrentHealth = damage >= Creature.CurrentHealth ? 0u : Creature.CurrentHealth - damage;
        }
    }

    /// <summary>The threat every player starts an encounter's list with.</summary>
    private static readonly float Seed = new Avalon.World.Public.Combat.CombatConfig().InitialThreatSeed;

    private static float ThreatOf(MapInstance instance, Creature creature, IUnit attacker) =>
        instance.CombatService.GetEncounterFor(creature)!.GetThreatList(creature)[attacker];

    // ---- the base: weapon and stat ----

    [Fact]
    public void Deal_cleave_with_a_starter_sword_from_the_attack_stat_and_the_weapon_roll()
    {
        var rng = ScriptedCombatRandom.Plain().Longs(5);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_101, Sword, Cleave());
        Creature target = AddCreature(instance, 506_901, new Vector3(0f, 0f, 2f));

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        Assert.Equal(46u, warrior.Character.Stats!.Value.AttackDamage);
        Assert.Equal(100u - 30u, target.CurrentHealth);   // floor(12 + 0.3 x 46 + 1.0 x 5)
        Assert.Equal([(4L, 7L)], rng.WeaponRolls);
    }

    [Fact]
    public void Deal_cleave_unarmed_from_the_attack_stat_alone()
    {
        var rng = ScriptedCombatRandom.Plain();
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_111, weapon: null, Cleave());
        Creature target = AddCreature(instance, 506_911, new Vector3(0f, 0f, 2f));

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        Assert.Equal(100u - 25u, target.CurrentHealth);   // floor(25.8)
        Assert.Empty(rng.WeaponRolls);
    }

    [Fact]
    public void Scale_a_spell_by_ability_damage_and_never_roll_the_weapon()
    {
        var rng = ScriptedCombatRandom.Plain();
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, random: rng);
        var row = new Avalon.Domain.Characters.Character
        {
            Id = new CharacterId(506_121), AccountId = new AccountId(1), Name = "Wizard", Class = CharacterClass.Wizard,
            CreationDate = DateTime.UtcNow,
        };
        var wizard = new CharacterEntity(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, row,
            new Avalon.World.Configuration.RegenConfiguration()) { Data = row };
        MapInstanceClient caster = Join(instance, wizard);
        wizard.PowerType = PowerType.Mana;
        wizard.Container(InventoryType.Equipment).Load([Item(EquipmentSlots.MainHand, Staff)]);
        Assert.True(CharacterStatsRefresh.Apply(wizard, Rows, TestCombat.Factors, Find, CurrentValues.Refill));
        wizard.Spells.Load([AbilityTestData.Game(ArcaneCone())]);
        Creature target = AddCreature(instance, 506_921, new Vector3(0f, 0f, 2f));

        handler.Execute(caster.Connection, new CCastAbilityPacket { AbilityId = 210 });

        Assert.Equal(69u, wizard.Stats!.Value.AbilityDamage);
        Assert.Equal(100u - 29u, target.CurrentHealth);   // floor(12 + 0.25 x 69)
        Assert.Empty(rng.WeaponRolls);
    }

    // ---- armour, both ways ----

    [Fact]
    public void Mitigate_a_creatures_swing_by_the_players_armour()
    {
        var rng = ScriptedCombatRandom.Plain();
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: rng);
        MapInstanceClient warrior = Join(instance, New(506_131));
        warrior.Character.Container(InventoryType.Equipment).Load([Item(EquipmentSlots.Chest, Plate)]);
        Assert.True(CharacterStatsRefresh.Apply(warrior.Character, Rows, TestCombat.Factors, Find, CurrentValues.Refill));
        Creature boar = AddCreature(instance, 506_931, new Vector3(1f, 0f, 0f), level: 3);
        uint before = warrior.Character.CurrentHealth;

        instance.CombatService.ApplyDamage(boar, warrior.Character, 10);

        // floor(10 x (1 - 24 / (24 + 50 + 10 x 3))) = floor(7.69)
        Assert.Equal(before - 7u, warrior.Character.CurrentHealth);
    }

    [Fact]
    public void Mitigate_a_players_hit_by_the_creatures_armour_and_count_threat_from_what_landed()
    {
        var rng = ScriptedCombatRandom.Plain();
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_141, weapon: null, Cleave());
        Creature target = AddCreature(instance, 506_941, new Vector3(0f, 0f, 2f), armor: 60);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        // 25.8 x (1 - 60 / (60 + 50 + 10 x 1)) = 12.9, floored
        Assert.Equal(100u - 12u, target.CurrentHealth);
        Assert.Equal(Seed + 12f * 2.0f, ThreatOf(instance, target, warrior.Character));   // Warrior threat x2
    }

    // ---- dodge and crit ----

    [Fact]
    public void Deal_nothing_on_a_dodge_but_still_add_threat_for_the_base_and_form_the_encounter()
    {
        var rng = new ScriptedCombatRandom(0.0);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_151, weapon: null, Cleave());
        Creature target = AddCreature(instance, 506_951, new Vector3(0f, 0f, 2f), dodge: 30f);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        Assert.Equal(100u, target.CurrentHealth);
        Assert.Equal(0, ((CountingWoundScript)target.Script!).Hits);
        Assert.NotNull(instance.CombatService.GetEncounterFor(target));
        Assert.Equal(Seed + 25.8f * 2.0f, ThreatOf(instance, target, warrior.Character), precision: 3);
        Assert.Equal(0u, warrior.Character.CurrentPower);   // no Fury from a dodged hit
        Assert.True(warrior.Character.IsInCombat);
    }

    [Fact]
    public void Multiply_a_crit_by_the_crit_multiplier()
    {
        var rng = new ScriptedCombatRandom(0.99, 0.0, 0.99);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_161, weapon: null, Cleave());
        Creature target = AddCreature(instance, 506_961, new Vector3(0f, 0f, 2f));

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        Assert.Equal(100u - 38u, target.CurrentHealth);   // floor(25.8 x 1.5)
    }

    [Fact]
    public void Scale_a_heal_by_ability_damage_and_crit_it_but_never_mitigate_it()
    {
        var rng = new ScriptedCombatRandom(0.0);
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: rng);
        MapInstanceClient healer = Warrior(instance, 506_165);
        MapInstanceClient ally = Join(instance, New(506_166));
        ally.Character.Container(InventoryType.Equipment).Load([Item(EquipmentSlots.Chest, Plate)]);
        Assert.True(CharacterStatsRefresh.Apply(ally.Character, Rows, TestCombat.Factors, Find, CurrentValues.Refill));
        ally.Character.CurrentHealth = 10;
        AbilityTemplate mending = AbilityTestData.HealCircle(232);
        mending.ScalingStat = ScalingStat.Ability;
        mending.ScalingCoefficient = 0.6f;

        instance.CombatService.ApplyHeal(healer.Character, ally.Character, 40, AbilityTestData.Game(mending));

        // Ability damage 4: floor((40 + 0.6 x 4) x 1.5) = 63, and the armour 24 takes none of it.
        Assert.Equal(10u + 63u, ally.Character.CurrentHealth);
        Assert.Equal(1, rng.DoublesDrawn);
    }

    // ---- what reads the resolved damage ----

    [Fact]
    public void Gain_fury_from_the_health_the_mitigated_hit_took()
    {
        var rng = ScriptedCombatRandom.Plain();
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: rng);
        MapInstanceClient warrior = Join(instance, New(506_171));
        warrior.Character.PowerType = PowerType.Fury;
        warrior.Character.Container(InventoryType.Equipment).Load([Item(EquipmentSlots.Chest, Plate)]);
        Assert.True(CharacterStatsRefresh.Apply(warrior.Character, Rows, TestCombat.Factors, Find, CurrentValues.Refill));
        warrior.Character.CurrentPower = 0;
        Creature boar = AddCreature(instance, 506_971, new Vector3(1f, 0f, 0f), level: 1);
        uint before = warrior.Character.CurrentHealth;

        instance.CombatService.ApplyDamage(boar, warrior.Character, 100);

        // floor(100 x (1 - 24 / 84)) = 71 lost of 240
        Assert.Equal(before - 71u, warrior.Character.CurrentHealth);
        Assert.Equal(Fury.FromDamageTaken(71, before, warrior.Character.Health, 50f), warrior.Character.CurrentPower);
    }

    [Fact]
    public void Draw_no_roll_for_an_invulnerable_target_a_corpse_a_returning_creature_or_a_dead_heal_target()
    {
        var rng = new ScriptedCombatRandom();
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_181, Sword);
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

        instance.CombatService.ApplyDamage(warrior.Character, npc, 12, cleave);
        instance.CombatService.ApplyDamage(warrior.Character, corpse, 12, cleave);
        instance.CombatService.ApplyDamage(warrior.Character, returning, 12, cleave);
        instance.CombatService.ApplyHeal(warrior.Character, dead.Character, 40, AbilityTestData.Game(AbilityTestData.HealCircle(232)));

        Assert.Equal(0, rng.DoublesDrawn);
        Assert.Empty(rng.WeaponRolls);
    }

    // ---- review focus 1: mid-fight changes ----

    [Fact]
    public void Use_a_better_weapon_on_the_very_next_hit_after_equipping_it()
    {
        var rng = ScriptedCombatRandom.Plain().Longs(5, 20);
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_191, Sword);
        IAbility cleave = AbilityTestData.Game(Cleave());
        Creature target = AddCreature(instance, 506_991, new Vector3(0f, 0f, 2f), health: 1000);

        instance.CombatService.ApplyDamage(warrior.Character, target, 12, cleave);
        Assert.Equal(1000u - 30u, target.CurrentHealth);

        warrior.Character.Container(InventoryType.Equipment).Load([Item(EquipmentSlots.MainHand, BigSword)]);
        Assert.True(CharacterStatsRefresh.Apply(warrior.Character, Rows, TestCombat.Factors, Find, CurrentValues.KeepShare));
        instance.CombatService.ApplyDamage(warrior.Character, target, 12, cleave);

        Assert.Equal(1000u - 30u - 45u, target.CurrentHealth);   // floor(12 + 13.8 + 20)
        Assert.Equal([(4L, 7L), (20L, 20L)], rng.WeaponRolls);
    }

    [Fact]
    public void Use_the_new_levels_stats_on_the_next_hit_after_a_level_up()
    {
        var rng = ScriptedCombatRandom.Plain();
        using MapInstance instance = TestMapInstances.Build(NewWorld(), random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_192);
        IAbility cleave = AbilityTestData.Game(Cleave());
        Creature target = AddCreature(instance, 506_992, new Vector3(0f, 0f, 2f), health: 1000, armor: 60);

        instance.CombatService.ApplyDamage(warrior.Character, target, 12, cleave);
        Assert.Equal(1000u - 12u, target.CurrentHealth);   // 25.8 x (1 - 60 / 120)

        warrior.Character.Level = 2;
        Assert.True(CharacterStatsRefresh.Apply(warrior.Character, Rows, TestCombat.Factors, Find, CurrentValues.Refill));
        instance.CombatService.ApplyDamage(warrior.Character, target, 12, cleave);

        // Attack 50 now, and armour weighs less against level 2: (12 + 15) x (1 - 60 / 130) = 14.5
        Assert.Equal(1000u - 12u - 14u, target.CurrentHealth);
    }

    // ---- review focus 2: one cast, several units ----

    [Fact]
    public void Roll_each_unit_of_a_cleave_on_its_own_and_gain_only_for_the_ones_damaged()
    {
        // Nearest first: the first unit lands, the second dodges, the third lands.
        var rng = new ScriptedCombatRandom(0.99, 0.99, 0.99, 0.0, 0.99, 0.99, 0.99);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_201, weapon: null, Cleave());
        warrior.Character.CurrentPower = 0;
        Creature[] targets =
        [
            AddCreature(instance, 506_901_1, new Vector3(0f, 0f, 1.0f), dodge: 30f),
            AddCreature(instance, 506_901_2, new Vector3(0f, 0f, 1.6f), dodge: 30f),
            AddCreature(instance, 506_901_3, new Vector3(0f, 0f, 2.2f), dodge: 30f),
        ];

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        Assert.Equal([75u, 100u, 75u], targets.Select(t => t.CurrentHealth));
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
        var repository = Substitute.For<ICombatDataRepository>();
        repository.GetFormulasAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyCollection<CombatFormula>>(formulas.ToList()));
        repository.GetClassStatFactorsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<ClassStatFactors>>(CombatSeed.ClassFactors()));
        StaticData data = await TestStaticData.LoadAsync(TestStaticData.Repositories(combat: repository));
        formulas[0] = heavy;
        CombatPatch next = (CombatPatch)await data.PrepareAsync(ReloadArea.Combat);

        // The new generation is applied after the first unit's crit roll, in the middle of its resolve.
        var rng = new ReloadingRandom(afterDraws: 2, () => data.Apply(next));
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, world: NewWorld(data), random: rng);
        MapInstanceClient warrior = Warrior(instance, 506_211, weapon: null, Cleave());
        Creature first = AddCreature(instance, 506_921_1, new Vector3(0f, 0f, 1.0f), armor: 60);
        Creature second = AddCreature(instance, 506_921_2, new Vector3(0f, 0f, 2.0f), armor: 60);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        Assert.Equal(100u - 12u, first.CurrentHealth);    // the seeded formula: 25.8 x (1 - 60 / 120)
        Assert.Equal(100u - 24u, second.CurrentHealth);   // the reloaded one: 25.8 x (1 - 60 / 1070)
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
