using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Seeding;
using Avalon.World.Auras;
using Avalon.World.Characters;
using Avalon.World.Creatures.Locomotion;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Maps;
using Avalon.World.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>What auras do to a unit's stats: a character's refresh folds them in, a creature folds them on the spot.</summary>
public class AuraStatsIntegrationShould
{
    private static readonly DateTimeOffset s_t0 = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static ActiveAura Held(AuraTemplate template, uint stacks = 1) =>
        new(template, new ObjectGuid(), AuraSource.None, stacks, default,
            AuraSchedule.Start(s_t0, template.DurationMs, template.TickIntervalMs), template.DurationMs, s_t0.UtcDateTime);

    private static readonly ClassLevelStat s_warriorOne = new()
    { Class = CharacterClass.Warrior, Level = 1, BaseHp = 130, Stamina = 22, Strength = 23, Agility = 20, Intellect = 20 };

    private static readonly ItemTemplate s_plate = new()
    {
        Id = new ItemTemplateId(905_001),
        Name = "Plate",
        Slot = ItemSlotType.Chest,
        MaxStackSize = 1,
        StatType1 = StatType.Armor,
        StatValue1 = 30,
    };

    private static CharacterEntity Warrior()
    {
        CharacterEntity warrior = TestCharacters.New(905_101);
        warrior.Container(InventoryType.Equipment).Load([TestCharacters.Item(3, s_plate)]);
        Assert.True(CharacterStatsRefresh.Apply(warrior, [s_warriorOne], TestCombat.Factors,
            id => id == s_plate.Id ? s_plate : null, ResonanceTable.Empty, CurrentValues.EnterWorld, TestCombat.Formula));
        return warrior;
    }

    [Fact]
    public void Fold_a_characters_auras_into_its_next_stats_refresh()
    {
        CharacterEntity warrior = Warrior();
        Assert.Equal(30u + 2u, warrior.Stats!.Value.Armor);   // and Strength 23 x 0.1 (#1178)

        warrior.Auras.Add(Held(AuraTestData.Fortified()), s_t0);
        CharacterStatsRefresh.Apply(warrior, [s_warriorOne], TestCombat.Factors, id => id == s_plate.Id ? s_plate : null,
            ResonanceTable.Empty, CurrentValues.KeepShare, TestCombat.Formula);

        Assert.Equal(38u, warrior.Stats!.Value.Armor);   // round(32 x 1.2)
        Assert.True(warrior.SaveState.StatsDirty);
    }

    private static readonly ItemTemplate s_sword = new()
    {
        Id = new ItemTemplateId(905_002),
        Name = "Sword",
        Slot = ItemSlotType.MainHand,
        MaxStackSize = 1,
        StatType1 = StatType.Strength,
        StatValue1 = 3,
        StatType2 = StatType.IncreasedFireDamage,
        StatValue2 = 10,
        StatType3 = StatType.CritMultiplier,
        StatValue3 = 25,
    };

    private static readonly ClassLevelStat[] s_warriorRows = [s_warriorOne];

    private static ItemTemplate? Worn(ItemTemplateId id) => id == s_plate.Id ? s_plate : id == s_sword.Id ? s_sword : null;

    /// <summary>
    /// #1178: the refresh an aura with stat modifiers runs on a geared character (its gear's offence keys, its auras'
    /// offence) allocates nothing once its aura totals are worked out: the worn templates, the gear's stats and the main
    /// hand are read without a list, an iterator or a closure. Review Focus 3 (#1180): nor does its Resonance count, over
    /// the seeded table and then after a reload to more themes than the seed has, with ids far from their slots; the one
    /// refresh that meets the larger table reallocates the counts, and every one after it reuses them.
    /// </summary>
    [Fact]
    public async Task Refresh_a_geared_characters_stats_for_its_auras_without_allocating()
    {
        CharacterEntity warrior = TestCharacters.New(905_102);
        InventoryItem plate = TestCharacters.Item(3, s_plate);
        InventoryItem sword = TestCharacters.Item(EquipmentSlots.MainHand, s_sword);
        int iron = CatalogKeys.ModifierId("P12 T9");
        int thorn = CatalogKeys.ModifierId("P1 T9");
        int ember = CatalogKeys.ModifierId("P2 T9");
        warrior.ItemModifiers.Load([Copy(plate, iron), Copy(sword, thorn, ember)]);
        warrior.Container(InventoryType.Equipment).Load([plate, sword]);
        AuraTemplate emboldened = AuraTestData.Fortified();
        emboldened.Modifiers.Add(new AuraStatModifier { Stat = AuraStat.IncreasedDamage, Kind = AuraModifierKind.Flat, Value = 10f });
        emboldened.Modifiers.Add(new AuraStatModifier { Stat = AuraStat.IncreasedFireDamage, Kind = AuraModifierKind.More, Value = 20f });
        emboldened.Modifiers.Add(new AuraStatModifier { Stat = AuraStat.AttackSpeed, Kind = AuraModifierKind.Flat, Value = 5f });
        warrior.Auras.Add(Held(emboldened), s_t0);

        // The test data builds a new formula and factors on every read, so they are read once, outside the windows.
        IReadOnlyDictionary<CharacterClass, ClassStatFactors> factors = TestCombat.Factors;
        CombatFormula formula = TestCombat.Formula;
        // Two reads of the seeded table, taken in turn: a refresh keeps the counts over the same gear and table, so a new
        // table each time makes every refresh in the windows count again.
        ResonanceTable seeded = await ItemModifierRepositories.SeededResonance();
        ResonanceTable seededAgain = await ItemModifierRepositories.SeededResonance();
        int refreshes = 0;
        long allocated = ThreadAllocations.FewestOfThreeWindows(10, () => CharacterStatsRefresh.Apply(warrior, s_warriorRows,
            factors, Worn, refreshes++ % 2 == 0 ? seeded : seededAgain, CurrentValues.KeepShare, formula));

        Assert.Equal(0, allocated);
        Assert.Equal(1.2d, warrior.Stats!.Value.Offence.More(OffenceStat.Fire), precision: 6);
        Assert.Equal(
            [.. new[] { "Ember", "Iron", "Thorn" }.Select(name => new ThemeCount(ThemeId(name), 1)).OrderBy(pair => pair.ThemeId)],
            Pairs(warrior));

        // A reload to twenty themes, 1000-1019, one prefix row each; the worn affixes' rows are themed 1019, 1000 and 1007.
        Dictionary<int, int> worn = new() { [1019] = iron, [1000] = thorn, [1007] = ember };
        ItemModifier[] rows =
        [
            .. Enumerable.Range(1000, 20).Select(theme => new ItemModifier
            {
                Id = worn.GetValueOrDefault(theme, 905_000 + theme),
                GroupId = theme,
                Group = new ItemModifierGroup
                {
                    Id = theme, Kind = ItemModifierKind.Prefix, ThemeId = theme, Name = $"Theme {theme}",
                },
                Tier = 9,
                RequiredItemLevel = 1,
                Stats = [new ItemModifierStat { Stat = StatType.Stamina, Min = 1, Max = 1 }],
            }),
        ];
        ResonanceTable reloaded = new ItemModifierCatalog(rows, [], NullLogger.Instance).Resonance;
        CharacterStatsRefresh.Apply(warrior, s_warriorRows, factors, Worn, reloaded, CurrentValues.KeepShare, formula);

        Assert.Equal(20, TestCharacters.ResonanceOf(warrior).Table.Count);
        Assert.Equal([new ThemeCount(1000, 1), new ThemeCount(1007, 1), new ThemeCount(1019, 1)], Pairs(warrior));
        Assert.Equal(0, ThreadAllocations.FewestOfThreeWindows(10, () => CharacterStatsRefresh.Apply(warrior, s_warriorRows,
            factors, Worn, reloaded, CurrentValues.KeepShare, formula)));

        // Each affix with its seeded line's low end as its one value.
        static ItemInstance Copy(InventoryItem item, params int[] modifierIds) => new()
        {
            Identified = true,
            Id = item.InstanceId,
            TemplateId = item.TemplateId,
            Modifiers =
            [
                .. modifierIds.Select((id, ordinal) =>
                {
                    ItemModifierStat line = ItemModifierSeed.Stats().First(stat => stat.ModifierId == id && stat.Index == 0);
                    return TestModifiers.Modifier(item.InstanceId, id, (byte)ordinal, (0, line.Stat, line.Min, line.Min2));
                }),
            ],
        };

        static int ThemeId(string name) => ItemModifierSeed.Themes().Single(theme => theme.Name == name).Id;

        static List<ThemeCount> Pairs(CharacterEntity character)
        {
            List<ThemeCount> pairs = [];
            foreach (ThemeCount pair in TestCharacters.ResonanceOf(character))
                pairs.Add(pair);
            return pairs;
        }
    }

    private static Creature Wolf() => new()
    {
        Guid = new ObjectGuid(ObjectType.Creature, 905_901),
        Level = 3,
        Health = 100,
        CurrentHealth = 100,
        BaseMaxHealth = 100,
        Armor = 40,
        CritPct = 5f,
        Evasion = 2,
        BlockPct = 1f,
        DamageMin = 5,
        DamageMax = 9,
        Speed = 4f,
    };

    [Fact]
    public void Fold_a_creatures_auras_into_its_defence_on_the_spot()
    {
        Creature wolf = Wolf();
        AuraTemplate sundered = AuraTestData.Fortified(907);
        sundered.Kind = AuraKind.Harmful;
        sundered.Modifiers[0].Value = -25f;
        wolf.Auras.Add(Held(sundered), s_t0);

        AuraStatsRefresh.Apply(wolf, data: null, DateTime.UnixEpoch);

        Assert.Equal(new DefenderCombat(30, 2, 1f), Internal<DefenderCombat>(wolf, "Defence"));
    }

    /// <summary>
    /// A creature's attacker is built once and kept (#1179), so every change it reads (an aura, a level, a damage range)
    /// must reach the next read.
    /// </summary>
    [Fact]
    public void Fold_a_creatures_auras_level_and_damage_into_its_next_attack()
    {
        Creature wolf = Wolf();
        Assert.Equal((3u, 5f, 5u, 9u), Attack(wolf));
        AuraTemplate keen = AuraTestData.Fortified(908);
        keen.Modifiers[0].Stat = AuraStat.CritPct;
        keen.Modifiers[0].Kind = AuraModifierKind.Flat;
        keen.Modifiers[0].Value = 10f;
        wolf.Auras.Add(Held(keen), s_t0);

        AuraStatsRefresh.Apply(wolf, data: null, DateTime.UnixEpoch);
        Assert.Equal((3u, 15f, 5u, 9u), Attack(wolf));

        // Each input on its own, so every setter's own stale mark is what the next read depends on.
        wolf.DamageMin = 6;
        Assert.Equal((3u, 15f, 6u, 9u), Attack(wolf));
        wolf.DamageMax = 11;
        Assert.Equal((3u, 15f, 6u, 11u), Attack(wolf));
        wolf.Level = 4;
        Assert.Equal((4u, 15f, 6u, 11u), Attack(wolf));

        static (uint, float, uint, uint) Attack(Creature creature)
        {
            AttackerCombat a = Internal<AttackerCombat>(creature, "Combat");
            return (a.Level, a.CritPct, a.Weapon.Min1, a.Weapon.Max1);
        }
    }

    /// <summary>A creature's slow reaches the locomotion through its speed factor; the script's Speed stays the template's.</summary>
    [Fact]
    public void Slow_a_crippled_creature_and_bound_it_by_the_formula()
    {
        Creature wolf = Wolf();
        wolf.Auras.Add(Held(AuraTestData.Crippled()), s_t0);
        AuraStatsRefresh.Apply(wolf, data: null, DateTime.UnixEpoch);

        Assert.Equal(0.7f, wolf.SpeedFactor, precision: 4);
        Assert.Equal(2.8f, CreatureSpeed.Of(wolf), precision: 4);
        Assert.Equal(4f, wolf.Speed);

        wolf.Auras.Add(Held(AuraTestData.Crippled(908), stacks: 1), s_t0);
        wolf.Auras.Add(Held(AuraTestData.Crippled(909), stacks: 1), s_t0);
        AuraStatsRefresh.Apply(wolf, data: null, DateTime.UnixEpoch);
        Assert.Equal(0.5f, wolf.SpeedFactor, precision: 4);   // -90 points, held at the -50 floor
    }

    [Fact]
    public void Walk_a_crippled_creature_at_its_slowed_speed()
    {
        IMapNavigator navigator = Substitute.For<IMapNavigator>();
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns([new Vector3(20f, 0f, 0f)]);
        var locomotion = new WaypointLocomotion(_ => navigator);
        Creature wolf = Wolf();
        wolf.Position = Vector3.zero;
        wolf.Auras.Add(Held(AuraTestData.Crippled()), s_t0);
        AuraStatsRefresh.Apply(wolf, data: null, DateTime.UnixEpoch);

        locomotion.Register(wolf, radius: 0.5f);
        locomotion.MoveTo(wolf, new Vector3(20f, 0f, 0f));
        locomotion.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(2.8f, wolf.Position.x, precision: 3);
        Assert.Equal(2.8f, wolf.Velocity.magnitude, precision: 3);
    }

    /// <summary>A party rescale and a health aura compose: 100 x 1.6 x 1.1 = 176, keeping the share of health held.</summary>
    [Fact]
    public void Compose_a_creatures_health_aura_with_its_party_scaling()
    {
        Creature wolf = Wolf();
        wolf.CurrentHealth = 50;
        AuraTemplate vigour = AuraTestData.Fortified(910);
        vigour.Modifiers[0].Stat = AuraStat.MaxHealth;
        vigour.Modifiers[0].Value = 10f;

        wolf.Rescale(1.6);
        wolf.Auras.Add(Held(vigour), s_t0);
        AuraStatsRefresh.Apply(wolf, data: null, DateTime.UnixEpoch);

        Assert.Equal(176u, wolf.Health);
        Assert.Equal(88u, wolf.CurrentHealth);
    }

    /// <summary>Reads a World-side internal member (Combat or Defence), as SimulatorParityShould does.</summary>
    private static T Internal<T>(object unit, string property) =>
        (T)unit.GetType().GetProperty(property, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(unit)!;
}
