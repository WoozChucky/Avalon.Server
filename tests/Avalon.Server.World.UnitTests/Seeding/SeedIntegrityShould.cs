using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Abilities;
using Avalon.World.Characters;
using Avalon.World.Loot;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Seeding;

/// <summary>
/// Cross-checks between the seeded reference tables, over a real SQLite database created from the
/// model — so these assert what the shipped seed data actually contains, not what it was meant to.
/// </summary>
/// <remarks>
/// <c>EnsureCreated</c> applies the model's <c>HasData</c>, which covers creature templates and both
/// new reference tables. It does <em>not</em> cover <c>SpawnTableEntry</c>: those rows are inserted by
/// raw SQL in a migration rather than declared on the model, so they are invisible here. The check
/// that every spawn entry points at a real template therefore cannot be written at this level — what
/// stands in for it is <see cref="Seed_Every_Creature_The_Forest_Spawn_Table_References" />, which
/// pins the template ids that SQL depends on.
/// </remarks>
public class SeedIntegrityShould
{
    /// <summary>
    /// A creature whose level range reaches past the seeded <c>CreatureBaseStats</c> rows falls back to
    /// the highest one and logs a warning — survivable, but the creature is quietly the wrong strength.
    /// This keeps the two tables in step as either grows.
    /// </summary>
    [Fact]
    public void Cover_Every_Seeded_Creatures_Level_Range_With_Base_Stats_Rows()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        HashSet<ushort> levels = context.CreatureBaseStats
            .AsNoTracking()
            .Select(stat => stat.Level)
            .ToHashSet();

        Assert.NotEmpty(levels);

        List<CreatureTemplate> templates = context.CreatureTemplates.AsNoTracking().ToList();
        Assert.NotEmpty(templates);

        var uncovered = new List<string>();

        foreach (CreatureTemplate template in templates)
        {
            for (short level = template.MinLevel; level <= template.MaxLevel; level++)
            {
                if (!levels.Contains((ushort)level))
                {
                    uncovered.Add($"{template.Name} needs level {level}");
                }
            }
        }

        Assert.True(uncovered.Count == 0,
            "seeded creatures roll levels with no CreatureBaseStats row: " + string.Join(", ", uncovered));
    }

    /// <summary>
    /// Every rarity a seeded creature declares needs a multiplier row, or the deriver silently treats it
    /// as unscaled — an Elite with no row is just a Normal with a different label.
    /// </summary>
    [Fact]
    public void Cover_Every_Seeded_Creatures_Rarity_With_A_Multiplier_Row()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        HashSet<CreatureRarity> tiers = context.CreatureRarityModifiers
            .AsNoTracking()
            .Select(modifier => modifier.Rarity)
            .ToHashSet();

        var missing = context.CreatureTemplates
            .AsNoTracking()
            .Select(template => template.Rarity)
            .Distinct()
            .Where(rarity => !tiers.Contains(rarity))
            .ToList();

        Assert.True(missing.Count == 0,
            "seeded creatures use rarities with no multiplier row: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The forest spawn table is inserted by migration SQL against creature ids 4 to 10, so those
    /// templates existing is a precondition of that SQL rather than something EF enforces. A dangling
    /// reference throws inside <c>CreatureSpawner.Spawn</c> during instance construction, which stops
    /// every player entering the map — and the seed data is the only place it is visible beforehand.
    /// </summary>
    [Fact]
    public void Seed_Every_Creature_The_Forest_Spawn_Table_References()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        // The ids SeedForestRoster's INSERT names, and the rarity each is expected to carry.
        (uint Id, CreatureRarity Rarity)[] expected =
        [
            (4, CreatureRarity.Normal),
            (5, CreatureRarity.Normal),
            (6, CreatureRarity.Normal),
            (7, CreatureRarity.Normal),
            (8, CreatureRarity.Elite),
            (9, CreatureRarity.Rare),
            (10, CreatureRarity.Boss),
        ];

        List<CreatureTemplate> templates = context.CreatureTemplates.AsNoTracking().ToList();

        foreach ((uint id, CreatureRarity rarity) in expected)
        {
            CreatureTemplate? template = templates.SingleOrDefault(t => t.Id.Value == id);

            Assert.NotNull(template);
            Assert.Equal(rarity, template!.Rarity);
            Assert.False(string.IsNullOrWhiteSpace(template.Name),
                $"creature {id} is referenced by the forest spawn table but has no name");
        }
    }

    /// <summary>
    /// The town's authored spawns must point at real creature templates, and those templates must be
    /// the unkillable kind. A dangling <c>CreatureTemplateId</c> costs one NPC — placement catches and
    /// skips it — but a town NPC that is not <c>Invulnerable</c> is a bug no log line reports: it is
    /// simply a killable innkeeper.
    /// </summary>
    [Fact]
    public void Point_Every_Town_Spawn_At_A_Real_Template_That_Cannot_Be_Killed()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        List<MapCreatureSpawn> spawns = context.MapCreatureSpawns.AsNoTracking().ToList();
        Assert.NotEmpty(spawns);

        List<CreatureTemplate> templates = context.CreatureTemplates.AsNoTracking().ToList();

        foreach (MapCreatureSpawn spawn in spawns)
        {
            CreatureTemplate? template = templates
                .SingleOrDefault(t => t.Id.Value == spawn.CreatureTemplateId.Value);

            Assert.True(template is not null,
                $"map spawn {spawn.Id.Value} points at creature template "
                + $"{spawn.CreatureTemplateId.Value}, which is not seeded");

            Assert.True(template!.Invulnerable,
                $"{template.Name} is placed as a town NPC but is not Invulnerable - players could kill it");

            Assert.Equal("TownNpcScript", template.ScriptName);
        }
    }

    /// <summary>
    /// Map 1's whole population today is seven town NPCs: Uriel, Borin, the Innkeeper, Marta the
    /// banker (#463), and the three vendors (#432). Pinning the count and the map catches a seed
    /// edit that drops one, or quietly hangs NPCs off the wrong map.
    /// </summary>
    [Fact]
    public void Place_The_Seven_Town_Npcs_On_Map_One()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        List<MapCreatureSpawn> spawns = context.MapCreatureSpawns.AsNoTracking().ToList();

        Assert.Equal(7, spawns.Count);
        Assert.All(spawns, spawn => Assert.Equal(1u, spawn.MapTemplateId.Value));

        Assert.Equal(
            [1ul, 2ul, 3ul, 11ul, 12ul, 13ul, 14ul],
            spawns.Select(spawn => spawn.CreatureTemplateId.Value).OrderBy(id => id).ToArray());
    }

    /// <summary>
    /// Every forest creature derives its experience rather than authoring it, so a stray value would
    /// quietly opt one creature out of level scaling and the band falloff would then apply to a constant.
    /// </summary>
    [Fact]
    public void Leave_The_Forest_Rosters_Experience_Unauthored_So_It_Derives()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        // Materialised first: CreatureTemplateId is a value object, so Id.Value cannot translate to SQL.
        var authored = context.CreatureTemplates
            .AsNoTracking()
            .ToList()
            // Town NPCs are invulnerable and author 0, because a creature that cannot die cannot pay
            // out; Marta (11) is one, although her id is above the forest roster's.
            .Where(template => template.Id.Value >= 4 && !template.Invulnerable && template.Experience is not null)
            .Select(template => template.Name)
            .ToList();

        Assert.True(authored.Count == 0,
            "forest creatures should derive experience from level, but these author it: "
            + string.Join(", ", authored));
    }

    private static readonly LootTableId ForestCommon = new(1);

    private static readonly LootTableId ForestWeapons = new(9);

    private static readonly LootTableId ForestScrolls = new(10);

    private static readonly LootTableId ForestArmour = new(11);

    private static readonly ItemSubClass[] ArmourSubClasses =
        [ItemSubClass.Helmet, ItemSubClass.Chest, ItemSubClass.Legs, ItemSubClass.Gloves, ItemSubClass.Boots];

    /// <summary>
    /// Issue #460: every creature a player can kill drops something, and nothing drops from a town
    /// NPC, which cannot die. A new hostile template without a table would be a creature worth nothing.
    /// </summary>
    [Fact]
    public void Give_Every_Killable_Seeded_Creature_A_Loot_Table_And_Gold()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        List<CreatureTemplate> templates = context.CreatureTemplates.AsNoTracking().ToList();

        Assert.All(templates.Where(t => !t.Invulnerable), t =>
        {
            Assert.NotNull(t.LootTableId);
            Assert.InRange(t.MinGold, 1, t.MaxGold);
        });
        Assert.All(templates.Where(t => t.Invulnerable), t =>
        {
            Assert.Null(t.LootTableId);
            Assert.Equal(0, t.MaxGold);
        });
    }

    /// <summary>The seeded tables are content the server refuses silently if they are wrong, so load them as the server does.</summary>
    [Fact]
    public void Load_Every_Seeded_Loot_Table_Without_Refusing_Any()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        List<LootTable> tables = context.LootTables.AsNoTracking().Include(t => t.Entries).ToList();
        var catalog = new LootCatalog(tables, NullLoggerFactory.Instance);

        Assert.Empty(catalog.Refused);
        Assert.Equal(11, catalog.TableCount);
        // Table 1: 2. Tables 2-8: 6 each. Table 9: 4 weapons. Table 10: 3 scrolls. Table 11: 20 armour pieces.
        Assert.Equal(2 + (7 * 6) + 4 + 3 + 20, catalog.EntryCount);
        Assert.All(context.CreatureTemplates.AsNoTracking().ToList().Where(t => t.LootTableId is not null),
            t => Assert.True(catalog.TryGet(t.LootTableId!, out _), $"{t.Name} names a table the catalog does not hold"));
    }

    /// <summary>
    /// Every creature table rolls its own potions, then the shared pools: the common table, the
    /// weapon and scroll groups (each drops exactly one entry when it rolls, so it is referenced at
    /// the chance it should drop with), and the armour table, rolled every kill.
    /// </summary>
    [Fact]
    public void Reference_The_Shared_Pools_From_Every_Creature_Table()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        HashSet<int> creatureTables = context.CreatureTemplates.AsNoTracking().ToList()
            .Where(t => t.LootTableId is not null).Select(t => t.LootTableId!.Value).ToHashSet();
        List<LootTable> tables = context.LootTables.AsNoTracking().Include(t => t.Entries).ToList()
            .Where(t => creatureTables.Contains(t.Id.Value)).ToList();

        Assert.Equal(7, tables.Count);
        Assert.All(tables, table =>
        {
            Assert.Contains(table.Entries, e => e.GroupId is null && e.ItemTemplateId is not null);
            Assert.DoesNotContain(table.Entries, e => e.GroupId is not null);
            Assert.Contains(table.Entries, e => e.ReferenceTableId == ForestCommon);
            Assert.Single(table.Entries, e => e.ReferenceTableId == ForestWeapons && e.Chance == 2f);
            Assert.Single(table.Entries, e => e.ReferenceTableId == ForestScrolls && e.Chance == 10f);
            Assert.Single(table.Entries, e => e.ReferenceTableId == ForestArmour && e.Chance == 100f);
        });
    }

    [Fact]
    public void Seed_The_Forest_Weapons_As_One_Group_Of_One_Weapon_Per_Class()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        Dictionary<ItemTemplateId, ItemTemplate> items = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(i => i.Id);
        LootTable table = LoadTable(context, ForestWeapons);

        Assert.Equal(4, table.Entries.Count);
        Assert.All(table.Entries, e =>
        {
            Assert.Equal(1, e.GroupId);
            Assert.Equal(25f, e.Chance);
            Assert.Equal(ItemClass.Weapon, items[e.ItemTemplateId!].Class);
            Assert.Single(items[e.ItemTemplateId!].AllowedClasses);
        });
        Assert.Equal(
            Enum.GetValues<CharacterClass>().Order(),
            table.Entries.Select(e => items[e.ItemTemplateId!].AllowedClasses[0]).Order());
    }

    [Fact]
    public void Seed_The_Forest_Scrolls_As_One_Group_Of_Three()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        Dictionary<ItemTemplateId, ItemTemplate> items = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(i => i.Id);
        LootTable table = LoadTable(context, ForestScrolls);

        Assert.Equal(3, table.Entries.Count);
        Assert.Single(table.Entries.Select(e => e.Chance).Distinct());
        Assert.All(table.Entries, e =>
        {
            Assert.Equal(1, e.GroupId);
            ItemTemplate scroll = items[e.ItemTemplateId!];
            Assert.Equal(ItemClass.Consumable, scroll.Class);
            Assert.Equal(ItemSubClass.Scroll, scroll.SubClass);
        });
    }

    /// <summary>Every piece rolls on its own at 2 %, and the set covers each class in each of five slots exactly once.</summary>
    [Fact]
    public void Seed_The_Forest_Armour_As_Twenty_Independent_Pieces_One_Per_Class_And_Slot()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        Dictionary<ItemTemplateId, ItemTemplate> items = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(i => i.Id);
        LootTable table = LoadTable(context, ForestArmour);

        Assert.Equal(20, table.Entries.Count);
        Assert.All(table.Entries, e =>
        {
            Assert.Null(e.GroupId);
            Assert.Equal(2f, e.Chance);
            ItemTemplate piece = items[e.ItemTemplateId!];
            Assert.Equal(ItemClass.Armor, piece.Class);
            Assert.Contains(piece.SubClass, ArmourSubClasses);
            Assert.Equal(ItemRarity.Uncommon, piece.Rarity);
            Assert.Equal(SlotFor(piece.SubClass), piece.Slot);
            Assert.Single(piece.AllowedClasses);
        });

        var covered = table.Entries
            .Select(e => items[e.ItemTemplateId!])
            .Select(i => (i.AllowedClasses[0], i.SubClass))
            .ToHashSet();
        var expected = Enum.GetValues<CharacterClass>()
            .SelectMany(c => ArmourSubClasses.Select(s => (c, s)))
            .ToHashSet();
        Assert.True(expected.SetEquals(covered), "the armour table should hold one piece per class per slot");
    }

    /// <summary>
    /// Seeded kills, rolled the way the tick rolls them: one gold pile, at most one weapon and at most
    /// one scroll (each is a group, referenced once), and nothing names an item the seed does not
    /// have. Over 1000 kills of each creature, the 2 % weapon, the 10 % scroll and the 2 % armour
    /// pieces all turn up.
    /// </summary>
    [Fact]
    public void Roll_Every_Seeded_Creature_Into_One_Gold_Pile_At_Most_One_Weapon_And_At_Most_One_Scroll()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        List<ItemTemplate> items = context.ItemTemplates.AsNoTracking().ToList();
        List<LootTable> tables = context.LootTables.AsNoTracking().Include(t => t.Entries).ToList();
        HashSet<ulong> weapons = ItemsIn(tables, ForestWeapons);
        HashSet<ulong> scrolls = ItemsIn(tables, ForestScrolls);
        HashSet<ulong> armour = ItemsIn(tables, ForestArmour);
        var catalog = new LootCatalog(tables, NullLoggerFactory.Instance);
        var roller = new LootRoller(new LootRandom(new Random(460)), NullLogger<LootRoller>.Instance);

        List<CreatureTemplate> killable = context.CreatureTemplates.AsNoTracking().ToList()
            .Where(t => !t.Invulnerable).ToList();
        Assert.NotEmpty(killable);

        foreach (CreatureTemplate template in killable)
        {
            int weaponsDropped = 0, scrollsDropped = 0, armourDropped = 0;

            for (int i = 0; i < 1000; i++)
            {
                IReadOnlyList<RolledDrop> drops = roller.Roll(template, catalog, items);
                List<ulong> dropped = drops.Where(d => !d.IsGold).Select(d => d.ItemTemplateId!.Value).ToList();

                int weaponCount = dropped.Count(weapons.Contains);
                int scrollCount = dropped.Count(scrolls.Contains);
                Assert.InRange(weaponCount, 0, 1);
                Assert.InRange(scrollCount, 0, 1);
                weaponsDropped += weaponCount;
                scrollsDropped += scrollCount;
                armourDropped += dropped.Count(armour.Contains);

                Assert.Single(drops, d => d.IsGold);
                Assert.All(dropped, id => Assert.Contains(items, t => t.Id.Value == id));
            }

            Assert.True(weaponsDropped > 0, $"{template.Name} dropped no weapon in 1000 kills");
            Assert.True(scrollsDropped > 0, $"{template.Name} dropped no scroll in 1000 kills");
            Assert.True(armourDropped > 0, $"{template.Name} dropped no armour in 1000 kills");
        }
    }

    /// <summary>Owner decision: forest drops can be sold. Every item in the weapon, scroll and armour pools.</summary>
    [Fact]
    public void Let_Every_Forest_Pool_Item_Be_Sold()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        List<ItemTemplate> items = context.ItemTemplates.AsNoTracking().ToList();
        List<LootTable> tables = context.LootTables.AsNoTracking().Include(t => t.Entries).ToList();
        var pool = ItemsIn(tables, ForestWeapons)
            .Concat(ItemsIn(tables, ForestScrolls))
            .Concat(ItemsIn(tables, ForestArmour))
            .ToHashSet();

        Assert.Equal(27, pool.Count);   // items 5-31
        Assert.All(items.Where(i => pool.Contains(i.Id.Value)), item =>
        {
            Assert.False(item.Flags.HasFlag(ItemTemplateFlags.NoSell), $"{item.Name} is marked NoSell");
            Assert.True(item.SellPrice > 0, $"{item.Name} sells for nothing");
        });
    }

    /// <summary>
    /// Every class's armour carries Armor, so no class is unarmoured once mitigation reads it, and in
    /// each slot cloth (Wizard, Healer) is below leather (Hunter), which is below plate (Warrior).
    /// </summary>
    [Fact]
    public void Armour_Every_Class_With_Cloth_Below_Leather_Below_Plate_In_Each_Slot()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        Dictionary<ItemTemplateId, ItemTemplate> items = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(i => i.Id);
        List<ItemTemplate> pieces = LoadTable(context, ForestArmour).Entries.Select(e => items[e.ItemTemplateId!]).ToList();

        foreach (ItemSubClass slot in ArmourSubClasses)
        {
            uint ArmorFor(CharacterClass c) =>
                ArmorOf(Assert.Single(pieces, p => p.SubClass == slot && p.AllowedClasses[0] == c));

            uint wizard = ArmorFor(CharacterClass.Wizard), healer = ArmorFor(CharacterClass.Healer);
            uint hunter = ArmorFor(CharacterClass.Hunter), warrior = ArmorFor(CharacterClass.Warrior);

            Assert.True(wizard > 0 && healer > 0, $"{slot}: cloth has no Armor");
            Assert.True(Math.Max(wizard, healer) < hunter, $"{slot}: cloth {wizard}/{healer} is not below leather {hunter}");
            Assert.True(hunter < warrior, $"{slot}: leather {hunter} is not below plate {warrior}");
        }
    }

    /// <summary>Every creature table rolls both potions itself, outside any group.</summary>
    [Fact]
    public void Roll_Both_Potions_Ungrouped_On_Every_Creature_Table()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        List<LootTable> tables = context.LootTables.AsNoTracking().Include(t => t.Entries).ToList();

        for (int id = 2; id <= 8; id++)
        {
            LootTable table = Assert.Single(tables, t => t.Id == new LootTableId(id));
            foreach (ulong potion in new ulong[] { 1, 2 })
            {
                Assert.True(
                    table.Entries.Any(e => e.ItemTemplateId == new ItemTemplateId(potion) && e.GroupId is null),
                    $"loot table {id} does not roll item {potion} on its own");
            }
        }
    }

    /// <summary>Every pool weapon goes in the main hand, and the bow is the ranged one.</summary>
    [Fact]
    public void Seed_Every_Pool_Weapon_For_The_Main_Hand_With_A_Weapon_Sub_Class()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        Dictionary<ItemTemplateId, ItemTemplate> items = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(i => i.Id);
        List<ItemTemplate> weapons = LoadTable(context, ForestWeapons).Entries.Select(e => items[e.ItemTemplateId!]).ToList();

        Assert.All(weapons, w =>
        {
            Assert.Equal(ItemSlotType.MainHand, w.Slot);
            Assert.Contains(w.SubClass, new[] { ItemSubClass.OneHanded, ItemSubClass.TwoHanded, ItemSubClass.Ranged });
        });
        ItemTemplate bow = Assert.Single(weapons, w => w.AllowedClasses[0] == CharacterClass.Hunter);
        Assert.Equal(ItemSubClass.Ranged, bow.SubClass);
    }

    /// <summary>
    /// #463 final review: a character's stats come only from its class and level's ClassLevelStat
    /// row, and a level with no row changes nothing, so gear and level-ups silently stop working
    /// there. The seeded experience table ends at level 15, which makes level 16 reachable; every
    /// class needs a row at every level from 1 up to it, with no gaps.
    /// </summary>
    [Fact]
    public void Seed_class_stats_for_every_class_at_every_reachable_level()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        ushort topLevel = (ushort)(context.CharacterLevelExperiences.AsNoTracking().Max(e => e.Level) + 1);
        Assert.Equal((ushort)16, topLevel);

        List<ClassLevelStat> rows = context.ClassLevelStats.AsNoTracking().ToList();
        var missing = new List<string>();
        foreach (CharacterClass @class in Enum.GetValues<CharacterClass>())
        {
            for (ushort level = 1; level <= topLevel; level++)
            {
                if (!rows.Exists(r => r.Class == @class && r.Level == level))
                    missing.Add($"{@class} {level}");
            }
        }

        Assert.True(missing.Count == 0, "no ClassLevelStat row for: " + string.Join(", ", missing));
        Assert.All(rows, r => Assert.InRange(r.Level, (ushort)1, topLevel));
    }

    private static uint ArmorOf(ItemTemplate item) =>
        (uint)new (StatType? Type, uint? Value)[]
            {
                (item.StatType1, item.StatValue1), (item.StatType2, item.StatValue2), (item.StatType3, item.StatValue3),
                (item.StatType4, item.StatValue4), (item.StatType5, item.StatValue5), (item.StatType6, item.StatValue6),
                (item.StatType7, item.StatValue7), (item.StatType8, item.StatValue8), (item.StatType9, item.StatValue9),
                (item.StatType10, item.StatValue10),
            }
            .Where(s => s.Type == StatType.Armor)
            .Sum(s => (long)(s.Value ?? 0));

    private static LootTable LoadTable(WorldDbContext context, LootTableId id) =>
        Assert.Single(context.LootTables.AsNoTracking().Include(t => t.Entries).ToList(), t => t.Id == id);

    private static HashSet<ulong> ItemsIn(IEnumerable<LootTable> tables, LootTableId id) =>
        tables.Single(t => t.Id == id).Entries.Where(e => e.ItemTemplateId is not null)
            .Select(e => e.ItemTemplateId!.Value).ToHashSet();

    private static ItemSlotType SlotFor(ItemSubClass subClass) => subClass switch
    {
        ItemSubClass.Helmet => ItemSlotType.Head,
        ItemSubClass.Chest => ItemSlotType.Chest,
        ItemSubClass.Legs => ItemSlotType.Legs,
        ItemSubClass.Gloves => ItemSlotType.Hands,
        ItemSubClass.Boots => ItemSlotType.Feet,
        _ => throw new ArgumentOutOfRangeException(nameof(subClass), subClass, "not an armour slot"),
    };

    /// <summary>The starter kit (#164): three skills per class.</summary>
    private static readonly Dictionary<CharacterClass, uint[]> Kit = new()
    {
        [CharacterClass.Warrior] = [200, 201, 202],
        [CharacterClass.Wizard] = [210, 211, 212],
        [CharacterClass.Hunter] = [220, 221, 222],
        [CharacterClass.Healer] = [230, 231, 232],
    };

    [Fact]
    public void Seed_exactly_the_twelve_kit_abilities_and_none_of_the_old_ones()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        List<uint> ids = context.AbilityTemplates.AsNoTracking().AsEnumerable().Select(a => a.Id.Value).OrderBy(i => i).ToList();

        Assert.Equal(Kit.Values.SelectMany(v => v).OrderBy(i => i), ids);
    }

    [Fact]
    public void Load_every_kit_ability_through_the_catalog()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var catalog = new AbilityCatalog(context.AbilityTemplates.AsNoTracking().ToList(), NullLoggerFactory.Instance);

        Assert.Empty(catalog.Refused);
        Assert.Equal(12, catalog.Count);
    }

    [Fact]
    public void Give_each_class_its_own_three_kit_abilities_to_start_with()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        List<CharacterCreateInfo> infos = context.CharacterCreateInfos.AsNoTracking().ToList();
        Assert.Equal(Kit.Keys.OrderBy(c => c), infos.Select(i => i.Class).OrderBy(c => c));

        foreach (CharacterCreateInfo info in infos)
        {
            Assert.Equal(Kit[info.Class], info.StartingSpells.Select(s => s.Value));

            foreach (uint id in Kit[info.Class])
            {
                AbilityTemplate ability = context.AbilityTemplates.AsNoTracking().AsEnumerable().Single(a => a.Id.Value == id);
                Assert.Equal([info.Class], ability.AllowedClasses);
            }
        }
    }

    /// <summary>
    /// A kit skill its class's power pool cannot pay under the one power rule could never be cast.
    /// Every character enters with a full pool, so a pool holding exactly the cost must be enough, and
    /// the class's level-1 maximum (seeded ClassLevelStats through CharacterStatsCalculator, with
    /// nothing worn, as at creation) must hold at least the cost.
    /// </summary>
    [Fact]
    public void Price_every_kit_ability_in_a_power_its_class_can_pay()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        List<AbilityTemplate> abilities = context.AbilityTemplates.AsNoTracking().ToList();
        List<ClassLevelStat> levelStats = context.ClassLevelStats.AsNoTracking().ToList();
        List<ClassStatFactors> factors = context.ClassStatFactors.AsNoTracking().ToList();

        foreach ((CharacterClass cls, uint[] ids) in Kit)
        {
            ClassLevelStat levelOne = levelStats.Single(s => s.Class == cls && s.Level == 1);
            uint maxPower = CharacterStatsCalculator.Calculate(levelOne, [], factors.Single(f => f.Class == cls)).MaxPower;

            foreach (uint id in ids)
            {
                AbilityTemplate ability = abilities.Single(a => a.Id.Value == id);
                IUnit caster = Substitute.For<IUnit>();
                caster.PowerType.Returns(ClassPowerType.Of(cls));
                caster.CurrentPower.Returns(ability.Cost);

                Assert.True(AbilityCost.Check(caster, AbilityMetadataMapper.From(ability)) == CostCheck.Payable,
                    $"{cls} cannot pay for ability {id} '{ability.Name}' ({ability.Cost} power)");
                Assert.True(ability.Cost <= maxPower,
                    $"ability {id} '{ability.Name}' costs {ability.Cost}, above a level-1 {cls}'s {maxPower} power");
            }
        }
    }

    [Fact]
    public void Name_one_script_per_shape_on_every_kit_ability()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        Assert.All(context.AbilityTemplates.AsNoTracking().ToList(), a => Assert.Equal(a.Shape switch
        {
            AbilityShape.Circle => "CircleAbilityScript",
            AbilityShape.Cone => "ConeAbilityScript",
            _ => "ProjectileAbilityScript",
        }, a.SpellScript));
    }

    /// <summary>#526: Cleave is the one builder, at 8 per unit damaged; every other ability gains nothing.</summary>
    [Fact]
    public void Seed_a_power_gain_per_hit_on_cleave_only()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        Assert.All(context.AbilityTemplates.AsNoTracking().ToList(),
            a => Assert.Equal(a.Id.Value == 200 ? 8 : 0, a.PowerGainPerHit));
    }

    [Fact]
    public void Seed_only_the_healers_mending_circle_as_an_ally_heal()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        AbilityTemplate heal = Assert.Single(context.AbilityTemplates.AsNoTracking().ToList(), a => a.Affects == AbilityAffects.Ally);
        Assert.Equal(232u, heal.Id.Value);
        Assert.Equal(0.5f, heal.HealThreatPerHp);
        Assert.Equal(SpellEffect.Heal, heal.Effects);
    }

    /// <summary>#506: each kit ability's damage stat and coefficients, as the balance seed set them.</summary>
    [Theory]
    [InlineData(200u, ScalingStat.Attack, 0.3f, 1.0f)]
    [InlineData(201u, ScalingStat.Attack, 0.6f, 1.5f)]
    [InlineData(202u, ScalingStat.Attack, 0.5f, 1.0f)]
    [InlineData(210u, ScalingStat.Ability, 0.25f, 0f)]
    [InlineData(211u, ScalingStat.Ability, 0.8f, 0f)]
    [InlineData(212u, ScalingStat.Ability, 0.5f, 0f)]
    [InlineData(220u, ScalingStat.Attack, 0.3f, 1.0f)]
    [InlineData(221u, ScalingStat.Attack, 0.6f, 1.2f)]
    [InlineData(222u, ScalingStat.Attack, 0.4f, 0.8f)]
    [InlineData(230u, ScalingStat.Ability, 0.3f, 0f)]
    [InlineData(231u, ScalingStat.Ability, 0.5f, 0f)]
    [InlineData(232u, ScalingStat.Ability, 0.6f, 0f)]
    public void Seed_each_kit_abilitys_scaling(uint id, ScalingStat stat, float scaling, float weapon)
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        AbilityTemplate ability = context.AbilityTemplates.AsNoTracking().ToList().Single(a => a.Id.Value == id);
        Assert.Equal((stat, scaling, weapon), (ability.ScalingStat, ability.ScalingCoefficient, ability.WeaponCoefficient));
    }

    /// <summary>#506: the starter weapons roll 4-7, the forest weapons 7-11, and the two-handed Thornwood Staff 9-14.</summary>
    [Theory]
    [InlineData(32ul, 4u, 7u)]
    [InlineData(33ul, 4u, 7u)]
    [InlineData(34ul, 4u, 7u)]
    [InlineData(35ul, 4u, 7u)]
    [InlineData(5ul, 9u, 14u)]
    [InlineData(6ul, 7u, 11u)]
    [InlineData(7ul, 7u, 11u)]
    [InlineData(8ul, 7u, 11u)]
    public void Seed_the_weapon_damage_ranges(ulong id, uint min, uint max)
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        ItemTemplate item = context.ItemTemplates.AsNoTracking().ToList().Single(i => i.Id.Value == id);
        Assert.Equal(((uint?)min, (uint?)max), (item.DamageMin1, item.DamageMax1));
    }

    /// <summary>#506: creature armour rises linearly from 0 at level 1 to 30 at level 10, round(30 x (L - 1) / 9).</summary>
    [Fact]
    public void Seed_creature_armour_rising_linearly_to_thirty_at_level_ten()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        List<CreatureBaseStat> rows = context.CreatureBaseStats.AsNoTracking().ToList().OrderBy(r => r.Level).ToList();
        Assert.Equal(Enumerable.Range(1, 10).Select(l => (ushort)l), rows.Select(r => r.Level));
        Assert.Equal([0u, 3u, 7u, 10u, 13u, 17u, 20u, 23u, 27u, 30u], rows.Select(r => r.Armor));
    }

    /// <summary>#506: the rarity crit, dodge and block chances, in percentage points.</summary>
    [Theory]
    [InlineData(CreatureRarity.Normal, 0f, 0f, 0f)]
    [InlineData(CreatureRarity.Elite, 5f, 3f, 0f)]
    [InlineData(CreatureRarity.Rare, 8f, 5f, 5f)]
    [InlineData(CreatureRarity.Boss, 10f, 5f, 10f)]
    public void Seed_the_rarity_rolls(CreatureRarity rarity, float crit, float dodge, float block)
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        CreatureRarityModifier row = context.CreatureRarityModifiers.AsNoTracking().ToList().Single(r => r.Rarity == rarity);
        Assert.Equal((crit, dodge, block), (row.CritPct, row.DodgePct, row.BlockPct));
    }

    /// <summary>#506: the one formula row and the class factors, as the balance seed set them.</summary>
    [Fact]
    public void Seed_the_combat_formula_and_one_factors_row_per_class()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        CombatFormula f = Assert.Single(context.CombatFormulas.AsNoTracking().ToList());
        Assert.Equal((1, 50f, 10f, 0.75f, 1.5f, 0.5f, 50f, 30f, 50f),
            (f.Id, f.ArmorBase, f.ArmorPerLevel, f.ArmorCap, f.CritMultiplier, f.BlockMultiplier, f.CritCap, f.DodgeCap, f.BlockCap));

        var factors = context.ClassStatFactors.AsNoTracking().ToList().ToDictionary(r => r.Class);
        Assert.Equal(Enum.GetValues<CharacterClass>().Order(), factors.Keys.Order());
        ClassStatFactors w = factors[CharacterClass.Warrior];
        Assert.Equal((10u, 0d, 0d, (uint?)100u, 2d, 0d, 0.2d, 5.0f, 3.664f, 5.0f),
            (w.HpPerStamina, w.PowerPerIntellect, w.PowerPerAgility, w.FixedPower, w.AttackPerStrength, w.AttackPerAgility,
             w.AbilityPerIntellect, w.BaseBlock, w.BaseDodge, w.BaseCrit));
        ClassStatFactors h = factors[CharacterClass.Hunter];
        Assert.Equal((8u, 2d, 0.8d, (uint?)null, 0.5d, 1.5d, 0.5d, 0f, 4.35f, 5.0f),
            (h.HpPerStamina, h.PowerPerIntellect, h.PowerPerAgility, h.FixedPower, h.AttackPerStrength, h.AttackPerAgility,
             h.AbilityPerIntellect, h.BaseBlock, h.BaseDodge, h.BaseCrit));
    }
}
