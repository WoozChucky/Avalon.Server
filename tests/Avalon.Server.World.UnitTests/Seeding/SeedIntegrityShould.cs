using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.Utils;
using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.World.Abilities;
using Avalon.World.Auras;
using Avalon.World.Doodads;
using Avalon.World.Doodads.Scripts;
using Avalon.World.Loot;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;
using Avalon.World.Quests;
using Avalon.World.Scripts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using CreatureRarity = Avalon.World.Public.Enums.CreatureRarity;

namespace Avalon.Server.World.UnitTests.Seeding;

/// <summary>
/// Cross-checks between the seeded reference tables, over a real SQLite database created from the
/// model — so these assert what the shipped seed data actually contains, not what it was meant to.
/// </summary>
/// <remarks>
/// <c>EnsureCreated</c> applies the model's <c>HasData</c>, which covers creature templates and both
/// new reference tables. It does <em>not</em> cover spawn tables or procedural map configs: those are
/// seeded from Maps/ by ChunkCatalogSeeder at World start, whose own tests (ChunkCatalogSeederShould)
/// refuse an unknown creature id. <see cref="Seed_Every_Creature_The_Forest_Spawn_Table_References" />
/// still pins the template ids the forest's table names.
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
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var levels = context.CreatureBaseStats
            .AsNoTracking()
            .Select(stat => stat.Level)
            .ToHashSet();

        Assert.NotEmpty(levels);

        var templates = context.CreatureTemplates.AsNoTracking().ToList();
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
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var tiers = context.CreatureRarityModifiers
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
    /// The forest spawn table (Maps/spawn-tables.json) names creature ids 4 to 10, so those
    /// templates existing is a precondition of that file rather than something EF enforces. The World
    /// server's start-up seeder refuses a table naming a missing template, which stops the server; this
    /// pins, before any start, that the seed still has every template the forest's table names.
    /// </summary>
    [Fact]
    public void Seed_Every_Creature_The_Forest_Spawn_Table_References()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        // The ids the forest's spawn table names, and the rarity each is expected to carry.
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

        var templates = context.CreatureTemplates.AsNoTracking().ToList();

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
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var spawns = context.MapCreatureSpawns.AsNoTracking().ToList();
        Assert.NotEmpty(spawns);

        var templates = context.CreatureTemplates.AsNoTracking().ToList();

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
    /// A party instance holds at most <c>min(Game:MaxPartySize, map.MaxPlayers)</c>, so a procedural map seeded
    /// below the default party size turns away a whole party's later members with <c>InstanceFull</c> (#707: the
    /// forest was seeded at 1). Solo instances are keyed per character, so the cap never lets strangers share one.
    /// </summary>
    [Fact]
    public void Seat_A_Whole_Default_Party_On_Every_Seeded_Procedural_Map()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        int partySize = new Avalon.World.Configuration.GameConfiguration().MaxPartySize;
        var normals = context.MapTemplates.AsNoTracking().ToList()
            .Where(map => map.MapType == MapType.Normal)
            .ToList();

        Assert.Contains(normals, map => map.Id.Value == 2); // the forest
        Assert.All(normals, map => Assert.True(map.MaxPlayers is null || map.MaxPlayers >= partySize,
            $"{map.Name} seats {map.MaxPlayers}, below the default party size {partySize}"));
    }

    /// <summary>
    /// Forest content pass: the forest's reward band runs to 15, so a character is never out of band before the cap.
    /// Its spawns roll their levels from depth bands instead (Maps/ProceduralMaps/2.json), so the band does not
    /// constrain spawning.
    /// </summary>
    [Fact]
    public void Band_the_forests_rewards_from_level_one_to_fifteen()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        MapTemplate forest = context.MapTemplates.AsNoTracking().ToList().Single(m => m.Id.Value == 2);

        Assert.Equal((ushort?)1, forest.MinLevel);
        Assert.Equal((ushort?)15, forest.MaxLevel);
    }

    /// <summary>
    /// Map 1's whole population today is seven town NPCs: Uriel, Borin, the Innkeeper, Marta the
    /// banker (#463), and the three vendors (#432). Pinning the count and the map catches a seed
    /// edit that drops one, or quietly hangs NPCs off the wrong map.
    /// </summary>
    [Fact]
    public void Place_The_Seven_Town_Npcs_On_Map_One()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var spawns = context.MapCreatureSpawns.AsNoTracking().ToList();

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
        using var database = SqliteDatabase.World();
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

    private static readonly LootTableId s_forestCommon = new(1);

    private static readonly LootTableId s_forestWeapons = new(9);

    private static readonly LootTableId s_forestScrolls = new(10);

    private static readonly LootTableId s_forestArmour = new(11);

    private static readonly ItemSubClass[] s_armourSubClasses =
        [ItemSubClass.Helmet, ItemSubClass.Chest, ItemSubClass.Legs, ItemSubClass.Gloves, ItemSubClass.Boots];

    /// <summary>
    /// Issue #460: every creature a player can kill drops something, and nothing drops from a town
    /// NPC, which cannot die. A new hostile template without a table would be a creature worth nothing.
    /// </summary>
    [Fact]
    public void Give_Every_Killable_Seeded_Creature_A_Loot_Table_And_Gold()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var templates = context.CreatureTemplates.AsNoTracking().ToList();

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
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var tables = context.LootTables.AsNoTracking().Include(t => t.Entries).ToList();
        var catalog = new LootCatalog(tables, NullLoggerFactory.Instance);

        Assert.Empty(catalog.Refused);
        Assert.Equal(13, catalog.TableCount);
        // Table 1: 2. Tables 2-8: 6 each. Table 9: 4 weapons. Table 10: 3 scrolls. Table 11: 20 armour pieces.
        // Table 12, the forest chest: 6. Table 13, the forest barrel: 2.
        Assert.Equal(2 + (7 * 6) + 4 + 3 + 20 + 6 + 2, catalog.EntryCount);
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
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var creatureTables = context.CreatureTemplates.AsNoTracking().ToList()
            .Where(t => t.LootTableId is not null).Select(t => t.LootTableId!.Value).ToHashSet();
        var tables = context.LootTables.AsNoTracking().Include(t => t.Entries).ToList()
            .Where(t => creatureTables.Contains(t.Id.Value)).ToList();

        Assert.Equal(7, tables.Count);
        Assert.All(tables, table =>
        {
            Assert.Contains(table.Entries, e => e.GroupId is null && e.ItemTemplateId is not null);
            Assert.DoesNotContain(table.Entries, e => e.GroupId is not null);
            Assert.Contains(table.Entries, e => e.ReferenceTableId == s_forestCommon);
            Assert.Single(table.Entries, e => e.ReferenceTableId == s_forestWeapons && e.Chance == 2f);
            Assert.Single(table.Entries, e => e.ReferenceTableId == s_forestScrolls && e.Chance == 10f);
            Assert.Single(table.Entries, e => e.ReferenceTableId == s_forestArmour && e.Chance == 100f);
        });
    }

    [Fact]
    public void Seed_The_Forest_Weapons_As_One_Group_Of_One_Weapon_Per_Class()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var items = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(i => i.Id);
        LootTable table = LoadTable(context, s_forestWeapons);

        Assert.Equal(4, table.Entries.Count);
        Assert.All(table.Entries, e =>
        {
            Assert.Equal(1, e.GroupId);
            Assert.Equal(25f, e.Chance);
            Assert.Equal(ItemClass.Weapon, items[e.ItemTemplateId!].Class);
            Assert.Single(items[e.ItemTemplateId!].AllowedClasses);
        });
        Assert.Equal(
            CharacterClasses.Playable.Order(),
            table.Entries.Select(e => items[e.ItemTemplateId!].AllowedClasses[0]).Order());
    }

    [Fact]
    public void Seed_The_Forest_Scrolls_As_One_Group_Of_Three()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var items = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(i => i.Id);
        LootTable table = LoadTable(context, s_forestScrolls);

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
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var items = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(i => i.Id);
        LootTable table = LoadTable(context, s_forestArmour);

        Assert.Equal(20, table.Entries.Count);
        Assert.All(table.Entries, e =>
        {
            Assert.Null(e.GroupId);
            Assert.Equal(2f, e.Chance);
            ItemTemplate piece = items[e.ItemTemplateId!];
            Assert.Equal(ItemClass.Armor, piece.Class);
            Assert.Contains(piece.SubClass, s_armourSubClasses);
            Assert.Equal(ItemRarity.Uncommon, piece.Rarity);
            Assert.Equal(SlotFor(piece.SubClass), piece.Slot);
            Assert.Single(piece.AllowedClasses);
        });

        var covered = table.Entries
            .Select(e => items[e.ItemTemplateId!])
            .Select(i => (i.AllowedClasses[0], i.SubClass))
            .ToHashSet();
        var expected = CharacterClasses.Playable
            .SelectMany(c => s_armourSubClasses.Select(s => (c, s)))
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
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var items = context.ItemTemplates.AsNoTracking().ToList();
        var tables = context.LootTables.AsNoTracking().Include(t => t.Entries).ToList();
        HashSet<ulong> weapons = ItemsIn(tables, s_forestWeapons);
        HashSet<ulong> scrolls = ItemsIn(tables, s_forestScrolls);
        HashSet<ulong> armour = ItemsIn(tables, s_forestArmour);
        var catalog = new LootCatalog(tables, NullLoggerFactory.Instance);
        var roller = new LootRoller(new LootRandom(new Random(460)), NullLogger<LootRoller>.Instance);

        var killable = context.CreatureTemplates.AsNoTracking().ToList()
            .Where(t => !t.Invulnerable).ToList();
        Assert.NotEmpty(killable);

        foreach (CreatureTemplate template in killable)
        {
            int weaponsDropped = 0, scrollsDropped = 0, armourDropped = 0;

            for (int i = 0; i < 1000; i++)
            {
                IReadOnlyList<RolledDrop> drops = roller.Roll(template, catalog, items);
                var dropped = drops.Where(d => !d.IsGold).Select(d => d.ItemTemplateId!.Value).ToList();

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
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var items = context.ItemTemplates.AsNoTracking().ToList();
        var tables = context.LootTables.AsNoTracking().Include(t => t.Entries).ToList();
        var pool = ItemsIn(tables, s_forestWeapons)
            .Concat(ItemsIn(tables, s_forestScrolls))
            .Concat(ItemsIn(tables, s_forestArmour))
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
    /// each slot cloth (Wizard, Cleric) is below leather (Hunter), which is below plate (Warrior).
    /// </summary>
    [Fact]
    public void Armour_Every_Class_With_Cloth_Below_Leather_Below_Plate_In_Each_Slot()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var items = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(i => i.Id);
        var pieces = LoadTable(context, s_forestArmour).Entries.Select(e => items[e.ItemTemplateId!]).ToList();

        foreach (ItemSubClass slot in s_armourSubClasses)
        {
            uint ArmorFor(CharacterClass c) =>
                ArmorOf(Assert.Single(pieces, p => p.SubClass == slot && p.AllowedClasses[0] == c));

            uint wizard = ArmorFor(CharacterClass.Wizard), cleric = ArmorFor(CharacterClass.Cleric);
            uint hunter = ArmorFor(CharacterClass.Hunter), warrior = ArmorFor(CharacterClass.Warrior);

            Assert.True(wizard > 0 && cleric > 0, $"{slot}: cloth has no Armor");
            Assert.True(Math.Max(wizard, cleric) < hunter, $"{slot}: cloth {wizard}/{cleric} is not below leather {hunter}");
            Assert.True(hunter < warrior, $"{slot}: leather {hunter} is not below plate {warrior}");
        }
    }

    /// <summary>
    /// The forest's doodad tables: the chest rolls potions, then the creatures' shared pools (the common table and the
    /// armour table every time, the weapon group at 10 % and the scroll group at 25 %, richer than a kill); the barrel
    /// rolls a potion or two and nothing else.
    /// </summary>
    [Fact]
    public void Seed_the_forest_chest_and_barrel_tables_as_designed()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        static IEnumerable<(int, ulong?, int?, float, int, int, int?)> Rows(LootTable table) =>
            table.Entries.OrderBy(e => e.Sequence).Select(e =>
                (e.Sequence, e.ItemTemplateId?.Value, e.ReferenceTableId?.Value, e.Chance, e.MinCount, e.MaxCount, e.GroupId));

        LootTable chest = LoadTable(context, new LootTableId(12));
        Assert.Equal("Forest chest", chest.Name);
        Assert.Equal(
            [(1, 1ul, null, 60f, 1, 2, null), (2, 2ul, null, 40f, 1, 1, null), (3, null, 1, 100f, 1, 1, null),
             (4, null, 9, 10f, 1, 1, null), (5, null, 10, 25f, 1, 1, null), (6, null, 11, 100f, 1, 1, null)],
            Rows(chest));

        LootTable barrel = LoadTable(context, new LootTableId(13));
        Assert.Equal("Forest barrel", barrel.Name);
        Assert.Equal([(1, 1ul, null, 25f, 1, 1, null), (2, 2ul, null, 15f, 1, 1, null)], Rows(barrel));
    }

    /// <summary>Every creature table rolls both potions itself, outside any group.</summary>
    [Fact]
    public void Roll_Both_Potions_Ungrouped_On_Every_Creature_Table()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var tables = context.LootTables.AsNoTracking().Include(t => t.Entries).ToList();

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
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var items = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(i => i.Id);
        var weapons = LoadTable(context, s_forestWeapons).Entries.Select(e => items[e.ItemTemplateId!]).ToList();

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
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        ushort topLevel = (ushort)(context.CharacterLevelExperiences.AsNoTracking().Max(e => e.Level) + 1);
        Assert.Equal((ushort)16, topLevel);

        var rows = context.ClassLevelStats.AsNoTracking().ToList();
        var missing = new List<string>();
        foreach (CharacterClass @class in CharacterClasses.Playable)
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

    /// <summary>The starter kit (#164), the aura abilities (auras) and the crowd-control abilities (#770).</summary>
    private static readonly Dictionary<CharacterClass, uint[]> s_kit = new()
    {
        [CharacterClass.Warrior] = [200, 201, 202, 203, 204],
        [CharacterClass.Wizard] = [210, 211, 212, 213, 214],
        [CharacterClass.Hunter] = [220, 221, 222, 223, 224],
        [CharacterClass.Cleric] = [230, 231, 232, 233, 234, 235],
    };

    /// <summary>The twenty-one kit abilities and the nineteen creature abilities 300-318; none of the retired ones.</summary>
    [Fact]
    public void Seed_exactly_the_kit_abilities_and_the_creature_abilities_and_none_of_the_old_ones()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var ids = context.AbilityTemplates.AsNoTracking().AsEnumerable().Select(a => a.Id.Value).OrderBy(i => i).ToList();

        Assert.Equal(s_kit.Values.SelectMany(v => v).Concat(Enumerable.Range(300, 19).Select(i => (uint)i)).OrderBy(i => i), ids);
    }

    /// <summary>
    /// #163: the creature abilities exactly as the design's table has them, with the arcs chosen for the cones
    /// and the speeds chosen for the projectiles. Every one is creature-only (no class), free, EffectValue 0, and
    /// dealt from the creature's natural range through BaseDamageCoefficient; a basic (coefficient 1.0) lists the
    /// seeded 2.25 s swing interval, which is what it waits.
    /// </summary>
    [Theory]
    [InlineData(300u, "Gore", AbilityShape.Cone, 1.8f, 0f, 90f, 0f, false, 0u, 2250u, 1.0f)]
    [InlineData(301u, "Trample", AbilityShape.Circle, 0f, 2.5f, 0f, 0f, false, 0u, 10000u, 1.6f)]
    [InlineData(302u, "Bite", AbilityShape.Cone, 1.8f, 0f, 90f, 0f, false, 0u, 2250u, 1.0f)]
    [InlineData(303u, "Ravenous Claw", AbilityShape.Cone, 2.5f, 0f, 90f, 0f, false, 0u, 8000u, 1.8f)]
    [InlineData(304u, "Sting", AbilityShape.Cone, 1.5f, 0f, 90f, 0f, false, 0u, 2250u, 1.0f)]
    [InlineData(305u, "Blight Spit", AbilityShape.Projectile, 10f, 0f, 0f, 14f, false, 0u, 6000u, 1.4f)]
    [InlineData(306u, "Slam", AbilityShape.Cone, 1.8f, 0f, 90f, 0f, false, 0u, 2250u, 1.0f)]
    [InlineData(307u, "Rotting Burst", AbilityShape.Circle, 0f, 3f, 0f, 0f, false, 0u, 12000u, 1.5f)]
    [InlineData(308u, "Maul", AbilityShape.Cone, 2f, 0f, 90f, 0f, false, 0u, 2250u, 1.0f)]
    [InlineData(309u, "Rending Frenzy", AbilityShape.Cone, 2.5f, 0f, 100f, 0f, false, 0u, 9000u, 1.8f)]
    [InlineData(310u, "Howling Roar", AbilityShape.Circle, 0f, 5f, 0f, 0f, false, 1000u, 15000u, 2.0f)]
    [InlineData(311u, "Tusk Gore", AbilityShape.Cone, 2f, 0f, 90f, 0f, false, 0u, 2250u, 1.0f)]
    [InlineData(312u, "Earthsplitter", AbilityShape.Cone, 5f, 0f, 60f, 0f, false, 1200u, 14000u, 2.4f)]
    [InlineData(313u, "Thorn Volley", AbilityShape.Projectile, 12f, 0f, 0f, 16f, true, 0u, 10000u, 1.6f)]
    [InlineData(314u, "Bramble Lash", AbilityShape.Cone, 2.5f, 0f, 90f, 0f, false, 0u, 2250u, 1.0f)]
    [InlineData(315u, "Bramble Nova", AbilityShape.Circle, 0f, 6f, 0f, 0f, false, 1200u, 16000u, 2.5f)]
    [InlineData(316u, "Thornspray", AbilityShape.Cone, 5f, 0f, 120f, 0f, false, 0u, 8000u, 1.8f)]
    [InlineData(317u, "Venom Spit", AbilityShape.Projectile, 10f, 0f, 0f, 14f, false, 0u, 8000u, 0.6f)]
    [InlineData(318u, "Sundering Howl", AbilityShape.Circle, 0f, 6f, 0f, 0f, false, 0u, 18000u, 0f)]
    public void Seed_each_creature_ability_as_designed(uint id, string name, AbilityShape shape, float reach,
        float radius, float arc, float speed, bool pierce, uint castTime, uint cooldown, float baseDamage)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        AbilityTemplate a = context.AbilityTemplates.AsNoTracking().AsEnumerable().Single(t => t.Id.Value == id);

        Assert.Equal((name, shape, reach, radius, arc, speed, pierce, castTime, cooldown, baseDamage),
            (a.Name, a.Shape, a.Reach, a.Radius, a.ArcDegrees, a.ProjectileSpeed, a.Pierce, a.CastTime, a.Cooldown,
                a.BaseDamageCoefficient));
        Assert.Empty(a.AllowedClasses);
        Assert.Equal((0u, 0u, 0f, 0), (a.Cost, a.EffectValue, a.ScalingCoefficient, a.PowerGainPerHit));
        Assert.Equal(PowerType.None, a.CostPowerType);
        Assert.Equal(AbilityAffects.Hostile, a.Affects);
        Assert.Equal(AbilityAnchor.Caster, a.Anchor);
        Assert.Equal(shape == AbilityShape.Projectile ? AbilityAimMode.Cursor : AbilityAimMode.Movement, a.AimMode);
    }

    /// <summary>#163: each forest template runs its own script, which names exactly its seeded abilities, its basic first.</summary>
    [Theory]
    [InlineData(4, "ThornbackBoarScript", new uint[] { 300, 301 })]
    [InlineData(5, "GreyFenWolfScript", new uint[] { 302, 303 })]
    [InlineData(6, "BlightflySwarmlingScript", new uint[] { 304, 317, 305 })]
    [InlineData(7, "HuskOfTheWoldScript", new uint[] { 306, 307 })]
    [InlineData(8, "BramblemawAlphaScript", new uint[] { 308, 310, 318, 309 })]
    [InlineData(9, "OldTuskrootScript", new uint[] { 311, 312, 313 })]
    [InlineData(10, "MotherBrambleScript", new uint[] { 314, 315, 316 })]
    public void Run_each_forest_creature_on_its_own_script(int templateId, string script, uint[] abilities)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        CreatureTemplate template = context.CreatureTemplates.AsNoTracking().AsEnumerable()
            .Single(t => t.Id == new CreatureTemplateId((ulong)templateId));
        Assert.Equal(script, template.ScriptName);

        Type type = typeof(Avalon.World.Scripts.ScriptManager).Assembly.GetTypes().Single(t => t.Name == script);
        var kit = (Avalon.World.Creatures.CreatureAbilityKit)type.GetProperty("Kit")!.GetValue(null)!;
        Assert.Equal(abilities, new[] { kit.Basic }.Concat(kit.Specials).Select(i => i.Value));
        Assert.Equal(1.0f, context.AbilityTemplates.AsNoTracking().AsEnumerable()
            .Single(a => a.Id == kit.Basic).BaseDamageCoefficient);
    }

    /// <summary>
    /// #1178: every damaging kit ability names its damage type, the heals, buffs and Hush name none, and the creature
    /// abilities name one only where it differs from their creature's base type (null inherits it). Blightfly and
    /// Mother Bramble deal blight; every other template is physical.
    /// </summary>
    [Fact]
    public void Type_every_seeded_ability_and_creature_as_the_combat_foundation_maps_them()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var expectedAbilities = new Dictionary<uint, DamageType?>
        {
            [200] = DamageType.Physical,
            [201] = DamageType.Physical,
            [202] = DamageType.Physical,
            [203] = DamageType.Physical,
            [204] = DamageType.Physical,
            [210] = DamageType.Lightning,
            [211] = DamageType.Fire,
            [212] = DamageType.Cold,
            [213] = DamageType.Fire,
            [214] = DamageType.Cold,
            [220] = DamageType.Physical,
            [221] = DamageType.Physical,
            [222] = DamageType.Physical,
            [223] = DamageType.Physical,
            [224] = DamageType.Physical,
            [230] = DamageType.Lightning,
            [231] = DamageType.Lightning,
            [232] = null,
            [233] = null,
            [234] = null,
            [235] = null,
        };
        for (uint id = 300; id <= 318; id++)
            expectedAbilities[id] = id is 307 or 312 or 313 ? DamageType.Blight : null;

        var expectedCreatures = new Dictionary<ulong, DamageType>();
        for (ulong id = 1; id <= 14; id++)
            expectedCreatures[id] = id is 6 or 10 ? DamageType.Blight : DamageType.Physical;

        var abilities = context.AbilityTemplates.AsNoTracking().ToList();
        Assert.Equal(expectedAbilities, abilities.ToDictionary(a => a.Id.Value, a => a.DamageType));
        Assert.Equal(expectedCreatures,
            context.CreatureTemplates.AsNoTracking().AsEnumerable().ToDictionary(t => t.Id.Value, t => t.DamageType));
        Assert.Equal("Spark Bolt", abilities.Single(a => a.Id.Value == 210).Name);
    }

    [Fact]
    public void Load_every_kit_ability_through_the_catalog()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var auras = new AuraCatalog(context.AuraTemplates.AsNoTracking().Include(a => a.Modifiers).ToList(),
            static _ => null, NullLoggerFactory.Instance);
        var catalog = new AbilityCatalog(context.AbilityTemplates.AsNoTracking().ToList(), NullLoggerFactory.Instance, auras);

        Assert.Empty(auras.Refused);
        Assert.Empty(catalog.Refused);
        Assert.Equal(21 + 19, catalog.Count);
    }

    [Fact]
    public void Give_each_class_its_own_kit_abilities_to_start_with()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var infos = context.CharacterCreateInfos.AsNoTracking().ToList();
        Assert.Equal(s_kit.Keys.OrderBy(c => c), infos.Select(i => i.Class).OrderBy(c => c));

        foreach (CharacterCreateInfo info in infos)
        {
            Assert.Equal(s_kit[info.Class], info.StartingSpells.Select(s => s.Value));

            foreach (uint id in s_kit[info.Class])
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
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var abilities = context.AbilityTemplates.AsNoTracking().ToList();
        var levelStats = context.ClassLevelStats.AsNoTracking().ToList();
        var factors = context.ClassStatFactors.AsNoTracking().ToList();

        foreach ((CharacterClass cls, uint[] ids) in s_kit)
        {
            ClassLevelStat levelOne = levelStats.Single(s => s.Class == cls && s.Level == 1);
            uint maxPower = CharacterStatsCalculator.Calculate(levelOne, [], factors.Single(f => f.Class == cls), TestCombat.Formula).MaxPower;

            foreach (uint id in ids)
            {
                AbilityTemplate ability = abilities.Single(a => a.Id.Value == id);
                IUnit caster = Substitute.For<IUnit>();
                caster.PowerType.Returns(ClassPowerType.Of(cls));
                caster.CurrentPower.Returns(ability.Cost);

                Assert.True(AbilityCost.Check(caster, AbilityMetadataMapper.From(ability)) == CostCheck.Payable,
                    $"{cls} cannot pay for ability {id} '{ability.Name}' ({ability.Cost} power)");
                // #652: a cost is spent from the class's own pool, and a free ability names none.
                Assert.Equal(ability.Cost > 0 ? ClassPowerType.Of(cls) : PowerType.None, ability.CostPowerType);
                Assert.True(ability.Cost <= maxPower,
                    $"ability {id} '{ability.Name}' costs {ability.Cost}, above a level-1 {cls}'s {maxPower} power");
            }
        }
    }

    [Fact]
    public void Name_one_script_per_shape_on_every_kit_ability()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        Assert.All(context.AbilityTemplates.AsNoTracking().ToList(), a => Assert.Equal(a.Shape switch
        {
            AbilityShape.Circle => "CircleAbilityScript",
            AbilityShape.Cone => "ConeAbilityScript",
            _ => "ProjectileAbilityScript",
        }, a.ScriptName));
    }

    /// <summary>#526: Cleave is the one builder, at 8 per unit damaged; every other ability gains nothing.</summary>
    [Fact]
    public void Seed_a_power_gain_per_hit_on_cleave_only()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        Assert.All(context.AbilityTemplates.AsNoTracking().ToList(),
            a => Assert.Equal(a.Id.Value == 200 ? 8 : 0, a.PowerGainPerHit));
    }

    /// <summary>#770: each class's basic attack, the one ability a silence spares, is flagged Basic; nothing else is.</summary>
    [Fact]
    public void Flag_each_classs_basic_attack_and_no_other_ability_as_basic()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        Assert.Equal([200u, 210u, 220u, 230u], context.AbilityTemplates.AsNoTracking().AsEnumerable()
            .Where(a => (a.Flags & AbilityFlags.Basic) != 0).Select(a => a.Id.Value).Order());
    }

    /// <summary>The Cleric's three ally abilities; Mending Circle the only direct heal, Renew and Fortify aura-only (auras).</summary>
    [Fact]
    public void Seed_the_clerics_ally_abilities_with_mending_circle_the_only_direct_heal()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var allies = context.AbilityTemplates.AsNoTracking().ToList()
            .Where(a => a.Affects == AbilityAffects.Ally).OrderBy(a => a.Id.Value).ToList();
        Assert.Equal([232u, 233u, 234u], allies.Select(a => a.Id.Value));
        Assert.Equal([SpellEffect.Heal, SpellEffect.Buff, SpellEffect.Buff], allies.Select(a => a.Effects));
        Assert.All(allies, a => Assert.Equal(0.5f, a.HealThreatPerHp));
    }

    /// <summary>#506: each kit ability's damage stat and coefficients, as the balance seed set them.</summary>
    [Theory]
    [InlineData(200u, ScalingStat.Attack, 0.3f, 1.0f)]
    [InlineData(201u, ScalingStat.Attack, 0.6f, 1.5f)]
    [InlineData(202u, ScalingStat.Attack, 0.5f, 1.0f)]
    [InlineData(210u, ScalingStat.Ability, 0.25f, 0f)]
    [InlineData(211u, ScalingStat.Ability, 0.4f, 0f)]
    [InlineData(212u, ScalingStat.Ability, 0.5f, 0f)]
    [InlineData(220u, ScalingStat.Attack, 0.3f, 1.0f)]
    [InlineData(221u, ScalingStat.Attack, 0.6f, 1.2f)]
    [InlineData(222u, ScalingStat.Attack, 0.4f, 0.8f)]
    [InlineData(230u, ScalingStat.Ability, 0.3f, 0f)]
    [InlineData(231u, ScalingStat.Ability, 0.5f, 0f)]
    [InlineData(232u, ScalingStat.Ability, 0.6f, 0f)]
    [InlineData(203u, ScalingStat.Attack, 0.2f, 0.5f)]
    [InlineData(213u, ScalingStat.Ability, 0f, 0f)]
    [InlineData(223u, ScalingStat.Attack, 0.35f, 0.6f)]
    [InlineData(233u, ScalingStat.Ability, 0f, 0f)]
    [InlineData(234u, ScalingStat.Ability, 0f, 0f)]
    [InlineData(204u, ScalingStat.Attack, 0.15f, 0.3f)]
    [InlineData(214u, ScalingStat.Ability, 0.1f, 0f)]
    [InlineData(224u, ScalingStat.Attack, 0.2f, 0.3f)]
    [InlineData(235u, ScalingStat.Ability, 0f, 0f)]
    public void Seed_each_kit_abilitys_scaling(uint id, ScalingStat stat, float scaling, float weapon)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        AbilityTemplate ability = context.AbilityTemplates.AsNoTracking().ToList().Single(a => a.Id.Value == id);
        Assert.Equal((stat, scaling, weapon), (ability.ScalingStat, ability.ScalingCoefficient, ability.BaseDamageCoefficient));
    }

    /// <summary>#732: Flame Burst halved from 35 + 0.8 x ability damage to 18 + 0.4 x ability damage.</summary>
    [Fact]
    public void Seed_flame_burst_at_half_its_first_damage()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        AbilityTemplate burst = context.AbilityTemplates.AsNoTracking().ToList().Single(a => a.Id.Value == 211);
        Assert.Equal((18u, 0.4f, 25u, 3f), (burst.EffectValue, burst.ScalingCoefficient, burst.Cost, burst.Radius));
    }

    /// <summary>
    /// Auras: the seeded auras, each as designed (8-10 the forest shrine's outcomes, 12-15 the crowd control of #770,
    /// with the design's break chances in percent; the slows Crippled and Hexed take its slow row).
    /// </summary>
    [Theory]
    [InlineData(1u, "Bleed", AuraKind.Harmful, 12000u, 3000u, AuraPeriodicKind.Damage, 12f, ScalingStat.Attack, 0.25f, 0f, AuraStacking.Stack, 3u, CrowdControl.None, 0f, 0f)]
    [InlineData(2u, "Burn", AuraKind.Harmful, 9000u, 3000u, AuraPeriodicKind.Damage, 24f, ScalingStat.Ability, 0.6f, 0f, AuraStacking.Refresh, 1u, CrowdControl.None, 0f, 0f)]
    [InlineData(3u, "Crippled", AuraKind.Harmful, 6000u, 0u, AuraPeriodicKind.None, 0f, ScalingStat.Attack, 0f, 0f, AuraStacking.Refresh, 1u, CrowdControl.None, 0f, 0.5f)]
    [InlineData(4u, "Renew", AuraKind.Helpful, 12000u, 3000u, AuraPeriodicKind.Heal, 24f, ScalingStat.Ability, 0.4f, 0f, AuraStacking.Refresh, 1u, CrowdControl.None, 0f, 0f)]
    [InlineData(5u, "Fortified", AuraKind.Helpful, 30000u, 0u, AuraPeriodicKind.None, 0f, ScalingStat.Attack, 0f, 0f, AuraStacking.Refresh, 1u, CrowdControl.None, 0f, 0f)]
    [InlineData(6u, "Poison", AuraKind.Harmful, 9000u, 3000u, AuraPeriodicKind.Damage, 3f, ScalingStat.Attack, 0f, 1f, AuraStacking.Stack, 3u, CrowdControl.None, 0f, 0f)]
    [InlineData(7u, "Sundered", AuraKind.Harmful, 10000u, 0u, AuraPeriodicKind.None, 0f, ScalingStat.Attack, 0f, 0f, AuraStacking.Refresh, 1u, CrowdControl.None, 0f, 0f)]
    [InlineData(8u, "Blessed", AuraKind.Helpful, 120000u, 0u, AuraPeriodicKind.None, 0f, ScalingStat.Attack, 0f, 0f, AuraStacking.Refresh, 1u, CrowdControl.None, 0f, 0f)]
    [InlineData(9u, "Emboldened", AuraKind.Helpful, 120000u, 0u, AuraPeriodicKind.None, 0f, ScalingStat.Attack, 0f, 0f, AuraStacking.Refresh, 1u, CrowdControl.None, 0f, 0f)]
    [InlineData(10u, "Hexed", AuraKind.Harmful, 60000u, 0u, AuraPeriodicKind.None, 0f, ScalingStat.Attack, 0f, 0f, AuraStacking.Refresh, 1u, CrowdControl.None, 0f, 0.5f)]
    [InlineData(12u, "Stunned", AuraKind.Harmful, 1500u, 0u, AuraPeriodicKind.None, 0f, ScalingStat.Attack, 0f, 0f, AuraStacking.Refresh, 1u, CrowdControl.Stun, 10f, 0.5f)]
    [InlineData(13u, "Bound", AuraKind.Harmful, 3000u, 0u, AuraPeriodicKind.None, 0f, ScalingStat.Attack, 0f, 0f, AuraStacking.Refresh, 1u, CrowdControl.Root, 25f, 0.5f)]
    [InlineData(14u, "Frozen", AuraKind.Harmful, 2500u, 0u, AuraPeriodicKind.None, 0f, ScalingStat.Attack, 0f, 0f, AuraStacking.Refresh, 1u, CrowdControl.Root, 25f, 0.5f)]
    [InlineData(15u, "Silenced", AuraKind.Harmful, 3000u, 0u, AuraPeriodicKind.None, 0f, ScalingStat.Attack, 0f, 0f, AuraStacking.Refresh, 1u, CrowdControl.Silence, 0f, 0f)]
    public void Seed_each_aura_as_designed(uint id, string name, AuraKind kind, uint duration, uint interval,
        AuraPeriodicKind periodic, float periodicBase, ScalingStat stat, float coefficient, float baseDamage,
        AuraStacking stacking, uint maxStacks, CrowdControl control, float breakOnDamage, float breakPerSecond)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        AuraTemplate a = context.AuraTemplates.AsNoTracking().AsEnumerable().Single(t => t.Id.Value == id);

        Assert.Equal((name, name.ToLowerInvariant(), kind, duration, interval, periodic, periodicBase, stat, coefficient,
                baseDamage, stacking, maxStacks, (string?)null),
            (a.Name, a.Icon, a.Kind, a.DurationMs, a.TickIntervalMs, a.PeriodicKind, a.PeriodicBase, a.ScalingStat,
                a.ScalingCoefficient, a.BaseDamageCoefficient, a.Stacking, a.MaxStacks, a.ScriptName));
        Assert.Equal((control, breakOnDamage, breakPerSecond), (a.Control, a.BreakOnDamagePct, a.BreakPerSecondPct));
    }

    [Fact]
    public void Seed_exactly_ten_aura_modifiers()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        Assert.Equal(
            [(3u, AuraStat.MovementSpeed, AuraModifierKind.Flat, -30f), (5u, AuraStat.Armor, AuraModifierKind.Increased, 20f),
             (7u, AuraStat.Armor, AuraModifierKind.Increased, -25f), (8u, AuraStat.MaxHealth, AuraModifierKind.Increased, 10f),
             (9u, AuraStat.AttackDamage, AuraModifierKind.Increased, 10f), (9u, AuraStat.AbilityDamage, AuraModifierKind.Increased, 10f),
             (9u, AuraStat.IncreasedDamage, AuraModifierKind.Flat, 10f), (9u, AuraStat.IncreasedHealing, AuraModifierKind.Flat, 10f),
             (10u, AuraStat.Armor, AuraModifierKind.Increased, -10f), (10u, AuraStat.MovementSpeed, AuraModifierKind.Flat, -15f)],
            context.AuraStatModifiers.AsNoTracking().AsEnumerable().OrderBy(m => m.AuraId.Value).ThenBy(m => m.Stat)
                .Select(m => (m.AuraId.Value, m.Stat, m.Kind, m.Value)));
    }

    /// <summary>
    /// Auras: the aura abilities, each with its shape, timing, cost, direct amount and aura; 204-235 the crowd control
    /// of #770, which a dodge or a miss lands no control from: Hush (235) deals nothing and is pure silence (#1164).
    /// </summary>
    [Theory]
    [InlineData(203u, "Rend", AbilityShape.Cone, AbilityAimMode.Movement, 2.5f, 0f, 90f, 0f, 6000u, 10u, PowerType.Fury, SpellEffect.Damage, 8u, 1u)]
    [InlineData(213u, "Ignite", AbilityShape.Circle, AbilityAimMode.Cursor, 18f, 3f, 0f, 0f, 6000u, 20u, PowerType.Mana, SpellEffect.Debuff, 0u, 2u)]
    [InlineData(223u, "Crippling Shot", AbilityShape.Projectile, AbilityAimMode.Cursor, 25f, 0f, 0f, 14f, 8000u, 15u, PowerType.Energy, SpellEffect.Damage, 14u, 3u)]
    [InlineData(233u, "Renew", AbilityShape.Circle, AbilityAimMode.Cursor, 15f, 4f, 0f, 0f, 6000u, 15u, PowerType.Mana, SpellEffect.Buff, 0u, 4u)]
    [InlineData(234u, "Fortify", AbilityShape.Circle, AbilityAimMode.Movement, 0f, 8f, 0f, 0f, 20000u, 20u, PowerType.Mana, SpellEffect.Buff, 0u, 5u)]
    [InlineData(204u, "Shield Bash", AbilityShape.Cone, AbilityAimMode.Movement, 2.5f, 0f, 90f, 0f, 15000u, 15u, PowerType.Fury, SpellEffect.Damage, 6u, 12u)]
    [InlineData(214u, "Frost Bind", AbilityShape.Circle, AbilityAimMode.Cursor, 18f, 3f, 0f, 0f, 20000u, 25u, PowerType.Mana, SpellEffect.Damage, 6u, 14u)]
    [InlineData(224u, "Binding Shot", AbilityShape.Projectile, AbilityAimMode.Cursor, 25f, 0f, 0f, 14f, 15000u, 20u, PowerType.Energy, SpellEffect.Damage, 8u, 13u)]
    [InlineData(235u, "Hush", AbilityShape.Projectile, AbilityAimMode.Cursor, 18f, 0f, 0f, 17f, 20000u, 20u, PowerType.Mana, SpellEffect.Debuff, 0u, 15u)]
    [InlineData(317u, "Venom Spit", AbilityShape.Projectile, AbilityAimMode.Cursor, 10f, 0f, 0f, 14f, 8000u, 0u, PowerType.None, SpellEffect.Damage, 0u, 6u)]
    [InlineData(318u, "Sundering Howl", AbilityShape.Circle, AbilityAimMode.Movement, 0f, 6f, 0f, 0f, 18000u, 0u, PowerType.None, SpellEffect.Debuff, 0u, 7u)]
    public void Seed_each_aura_ability_as_designed(uint id, string name, AbilityShape shape, AbilityAimMode aim, float reach,
        float radius, float arc, float speed, uint cooldown, uint cost, PowerType pool, SpellEffect effects, uint value, uint aura)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        AbilityTemplate a = context.AbilityTemplates.AsNoTracking().AsEnumerable().Single(t => t.Id.Value == id);

        Assert.Equal((name, shape, aim, reach, radius, arc, speed, 0u, cooldown, cost, pool, effects, value, (uint?)aura),
            (a.Name, a.Shape, a.AimMode, a.Reach, a.Radius, a.ArcDegrees, a.ProjectileSpeed, a.CastTime, a.Cooldown, a.Cost,
                a.CostPowerType, a.Effects, a.EffectValue, a.AuraId?.Value));
    }

    /// <summary>
    /// Crippled on a character: its whole change from the base speed must stay under the client's prediction snap, as
    /// gear's must (CharacterMovementSpeedShould): 4 m/s x 0.30 x 0.1 s = 0.12 m.
    /// </summary>
    [Fact]
    public void Keep_every_seeded_slow_under_the_clients_snap()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        Avalon.Domain.World.CombatFormula formula = Avalon.Database.World.Seeding.CombatSeed.Formula();

        foreach (AuraStatModifier m in context.AuraStatModifiers.AsNoTracking().AsEnumerable().Where(m => m.Stat == AuraStat.MovementSpeed))
        {
            float speed = Avalon.World.Characters.CharacterMovement.SpeedFor(m.Value, formula);
            float drift = Math.Abs(speed - Avalon.World.Characters.CharacterMovement.BaseSpeed) * 0.1f;
            Assert.True(drift < Avalon.World.Characters.CharacterMovement.ClientSnapThreshold,
                $"aura {m.AuraId.Value} moves a character {drift} m over a round trip");
        }
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
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        ItemTemplate item = context.ItemTemplates.AsNoTracking().ToList().Single(i => i.Id.Value == id);
        Assert.Equal(((uint?)min, (uint?)max), (item.DamageMin1, item.DamageMax1));
    }

    /// <summary>
    /// #506: creature armour rises linearly from 0 at level 1 to 30 at level 10, round(30 x (L - 1) / 9). #1178: evasion
    /// is 10 and ward 8 per level, before the template's modifiers.
    /// </summary>
    [Fact]
    public void Seed_creature_defences_by_level()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var rows = context.CreatureBaseStats.AsNoTracking().ToList().OrderBy(r => r.Level).ToList();
        Assert.Equal(Enumerable.Range(1, 10).Select(l => (ushort)l), rows.Select(r => r.Level));
        Assert.Equal([0u, 3u, 7u, 10u, 13u, 17u, 20u, 23u, 27u, 30u], rows.Select(r => r.Armor));
        Assert.Equal(rows.Select(r => 10u * r.Level), rows.Select(r => r.Evasion));
        Assert.Equal(rows.Select(r => 8u * r.Level), rows.Select(r => r.Ward));
    }

    /// <summary>
    /// #506: the rarity crit and block chances, in percentage points; #770: the share of a stun's, root's, silence's and
    /// slow's duration each tier takes, in percent. #1178: no tier dodges any more (the forest's creatures carry it as
    /// evasion), and every tier's defence multiplier is 1, so armour carries over exactly.
    /// </summary>
    [Theory]
    [InlineData(CreatureRarity.Normal, 0f, 0f, 100f, 100f, 100f, 100f)]
    [InlineData(CreatureRarity.Elite, 5f, 0f, 100f, 100f, 100f, 100f)]
    [InlineData(CreatureRarity.Rare, 8f, 5f, 50f, 50f, 50f, 50f)]
    [InlineData(CreatureRarity.Boss, 10f, 10f, 0f, 50f, 0f, 50f)]
    public void Seed_the_rarity_rolls(CreatureRarity rarity, float crit, float block, float stun, float root,
        float silence, float slow)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        CreatureRarityModifier row = context.CreatureRarityModifiers.AsNoTracking().ToList().Single(r => r.Rarity == rarity);
        Assert.Equal((crit, block, 1f), (row.CritPct, row.BlockPct, row.DefenceMultiplier));
        Assert.Equal((stun, root, silence, slow),
            (row.StunDurationPct, row.RootDurationPct, row.SilenceDurationPct, row.SlowDurationPct));
    }

    /// <summary>
    /// #506: the one formula row and the class factors, as the balance seed set them. #1178: the evasion curve, the
    /// resistance bounds and the class-neutral attribute conversions; no class has a base dodge or block any more.
    /// </summary>
    [Fact]
    public void Seed_the_combat_formula_and_one_factors_row_per_class()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        CombatFormula f = Assert.Single(context.CombatFormulas.AsNoTracking().ToList());
        Assert.Equal((1, 50f, 10f, 0.75f, 1.5f, 0.5f, 50f, 50f),
            (f.Id, f.ArmorBase, f.ArmorPerLevel, f.ArmorCap, f.CritMultiplier, f.BlockMultiplier, f.CritCap, f.BlockCap));
        Assert.Equal((560f, 40f, 30f, 75f, -100f, 0.1f, 1.125f, 0.25f),
            (f.EvasionBase, f.EvasionPerLevel, f.EvasionCap, f.ResistanceCap, f.ResistanceFloor, f.ArmorPerStrength,
             f.EvasionPerAgility, f.WardPerIntellect));

        var factors = context.ClassStatFactors.AsNoTracking().ToList().ToDictionary(r => r.Class);
        Assert.Equal(CharacterClasses.Playable.Order(), factors.Keys.Order());
        ClassStatFactors w = factors[CharacterClass.Warrior];
        Assert.Equal((10L, 0d, 0d, (long?)100L, 2d, 0d, 0.2d, 5.0f),
            (w.HpPerStamina, w.PowerPerIntellect, w.PowerPerAgility, w.FixedPower, w.AttackPerStrength, w.AttackPerAgility,
             w.AbilityPerIntellect, w.BaseCrit));
        ClassStatFactors h = factors[CharacterClass.Hunter];
        Assert.Equal((8L, 2d, 0.8d, (long?)null, 0.5d, 1.5d, 0.5d, 5.0f),
            (h.HpPerStamina, h.PowerPerIntellect, h.PowerPerAgility, h.FixedPower, h.AttackPerStrength, h.AttackPerAgility,
             h.AbilityPerIntellect, h.BaseCrit));
    }

    /// <summary>
    /// #1178, the offence model (R17): each kit ability's tags, damage effectiveness, spell base and growth per level, as
    /// re-expressed from today's numbers. An attack's effectiveness carries its level-1 average over the weapon's roll;
    /// a spell's or heal's base and growth carry its level-1 and level-10 averages. Fortify and Hush deal and heal nothing.
    /// </summary>
    [Theory]
    [InlineData(200u, AbilityTags.Attack | AbilityTags.Melee | AbilityTags.Area, 315f, 0u, 0u, 0f)]
    [InlineData(201u, AbilityTags.Attack | AbilityTags.Melee | AbilityTags.Area, 614f, 0u, 0u, 0f)]
    [InlineData(202u, AbilityTags.Attack | AbilityTags.Projectile, 492f, 0u, 0u, 0f)]
    [InlineData(203u, AbilityTags.Attack | AbilityTags.Melee | AbilityTags.Area, 202f, 0u, 0u, 0f)]
    [InlineData(204u, AbilityTags.Attack | AbilityTags.Melee | AbilityTags.Area, 148f, 0u, 0u, 0f)]
    [InlineData(210u, AbilityTags.Spell | AbilityTags.Projectile, 100f, 14u, 20u, 0.70f)]
    [InlineData(211u, AbilityTags.Spell | AbilityTags.Area, 100f, 21u, 32u, 0.75f)]
    [InlineData(212u, AbilityTags.Spell | AbilityTags.Area, 100f, 26u, 40u, 0.80f)]
    [InlineData(213u, AbilityTags.Spell | AbilityTags.Area, 100f, 31u, 46u, 0.90f)]
    [InlineData(214u, AbilityTags.Spell | AbilityTags.Area, 100f, 6u, 9u, 0.45f)]
    [InlineData(220u, AbilityTags.Attack | AbilityTags.Projectile, 363f, 0u, 0u, 0f)]
    [InlineData(221u, AbilityTags.Attack | AbilityTags.Projectile, 690f, 0u, 0u, 0f)]
    [InlineData(222u, AbilityTags.Attack | AbilityTags.Area, 516f, 0u, 0u, 0f)]
    [InlineData(223u, AbilityTags.Attack | AbilityTags.Projectile, 495f, 0u, 0u, 0f)]
    [InlineData(224u, AbilityTags.Attack | AbilityTags.Projectile, 168f, 0u, 0u, 0f)]
    [InlineData(230u, AbilityTags.Spell | AbilityTags.Projectile, 100f, 12u, 17u, 0.45f)]
    [InlineData(231u, AbilityTags.Spell | AbilityTags.Area, 100f, 20u, 31u, 0.35f)]
    [InlineData(232u, AbilityTags.Heal | AbilityTags.Area, 100f, 30u, 44u, 0f)]
    [InlineData(233u, AbilityTags.Heal | AbilityTags.Area, 100f, 19u, 28u, 0f)]
    [InlineData(234u, AbilityTags.Area, 100f, 0u, 0u, 0f)]
    [InlineData(235u, AbilityTags.Projectile, 100f, 0u, 0u, 0f)]
    public void Seed_each_abilitys_offence_as_re_expressed(uint id, AbilityTags tags, float effectiveness, uint baseMin,
        uint baseMax, float growth)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        AbilityTemplate a = context.AbilityTemplates.AsNoTracking().ToList().Single(t => t.Id.Value == id);
        Assert.Equal((tags, effectiveness, baseMin, baseMax, growth),
            (a.Tags, a.DamageEffectiveness, a.SpellBaseMin, a.SpellBaseMax, a.SpellBasePerLevelPct));

        // Only Crippling Shot converts (half to cold) and only Binding Shot adds (2-4 blight).
        Assert.Equal(id == 223 ? (DamageType.Cold, 50f) : ((DamageType?)null, 0f), (a.ConvertToType, a.ConvertPct));
        Assert.Equal(id == 224 ? (DamageType.Blight, 2u, 4u) : ((DamageType?)null, 0u, 0u), (a.AddedType, a.AddedMin, a.AddedMax));
    }

    /// <summary>
    /// #1178, the offence model: the formula's attribute conversions, base crit, speed caps and bare hand; each weapon's
    /// attack time, today's level-1 basic pace with starter gear and Agility's new speed (a sword 792, a bow 801, a mace
    /// 784, a staff 800), on weapons only; and how much of its source's roll each ticking aura draws.
    /// </summary>
    [Fact]
    public void Seed_the_offence_formula_and_the_weapons_attack_times()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        CombatFormula f = Assert.Single(context.CombatFormulas.AsNoTracking().ToList());
        Assert.Equal((3.5f, 0.1f, 3.5f, 3.5f, 5u, 5f),
            (f.IncreasedPhysicalAttackPerStrength, f.AttackSpeedPerAgility, f.IncreasedSpellPerIntellect,
             f.IncreasedHealingPerIntellect, f.LifePerStamina, f.BaseCritPct));
        Assert.Equal((50f, 50f, 50f, 3u, 6u, 816u),
            (f.AttackSpeedCap, f.CastSpeedCap, f.CooldownRecoveryCap, f.UnarmedDamageMin, f.UnarmedDamageMax,
             f.UnarmedAttackTimeMs));

        var attackTimes = new Dictionary<ulong, uint>
        {
            [4] = 792,
            [7] = 792,
            [32] = 792,
            [6] = 801,
            [34] = 801,
            [8] = 784,
            [35] = 784,
            [5] = 800,
            [33] = 800,
        };
        Assert.Equal(attackTimes, context.ItemTemplates.AsNoTracking().AsEnumerable().Where(i => i.AttackTimeMs is not null)
            .ToDictionary(i => i.Id.Value, i => i.AttackTimeMs!.Value));

        var periodic = new Dictionary<uint, float> { [1] = 21f, [2] = 100f, [4] = 100f, [6] = 100f };
        Assert.All(context.AuraTemplates.AsNoTracking().AsEnumerable(),
            a => Assert.Equal(periodic.GetValueOrDefault(a.Id.Value), a.PeriodicEffectiveness));
    }

    /// <summary>
    /// #1178 (R17): each hostile creature's defences, authored by its type. Armour carries over unchanged; evasion is new
    /// on the Blightfly and carries the Elite, Rare and Boss dodge over on the others; Mother Bramble alone has a ward.
    /// The Husk takes more from fire, and the Blightfly, the Husk and Mother Bramble resist blight.
    /// </summary>
    [Theory]
    [InlineData(4, 1f, 0f, 0f, 0f, 0f, 0f, 0f)]       // Thornback Boar, beast
    [InlineData(5, 1f, 0f, 0f, 0f, 0f, 0f, 0f)]       // Grey Fen Wolf, beast
    [InlineData(6, 1f, 0.5f, 0f, 0f, 0f, 0f, 25f)]    // Blightfly Swarmling, critter
    [InlineData(7, 1f, 0f, 0f, -25f, 0f, 0f, 50f)]    // Husk of the Wold, undead
    [InlineData(8, 1f, 0.55f, 0f, 0f, 0f, 0f, 0f)]    // Bramblemaw Alpha, beast (Elite)
    [InlineData(9, 1f, 0.8f, 0f, 0f, 0f, 0f, 0f)]     // Old Tuskroot, beast (Rare)
    [InlineData(10, 1f, 0.8f, 1f, 0f, 0f, 0f, 40f)]   // Mother Bramble, elemental (Boss)
    public void Seed_each_creatures_defences_as_re_expressed(int templateId, float armorModifier, float evasionModifier,
        float wardModifier, float fire, float cold, float lightning, float blight)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        CreatureTemplate t = context.CreatureTemplates.AsNoTracking().AsEnumerable()
            .Single(c => c.Id.Value == (ulong)templateId);
        Assert.Equal((armorModifier, evasionModifier, wardModifier, fire, cold, lightning, blight),
            (t.ArmorModifier, t.EvasionModifier, t.WardModifier, t.FireResistance, t.ColdResistance, t.LightningResistance,
             t.BlightResistance));
    }

    /// <summary>
    /// #1178 (R17): today's chance to dodge becomes the same chance to evade, within half a point, through the seeded
    /// formula. A class's evasion comes from its bare Agility, against a creature of its level, at levels 1 and 10; the
    /// forest's Elite, Rare and Boss carry their tier's former dodge as evasion, against a player of their level.
    /// </summary>
    [Theory]
    [InlineData("Warrior", 1, 3.664)]
    [InlineData("Wizard", 1, 3.25)]
    [InlineData("Hunter", 1, 4.35)]
    [InlineData("Cleric", 1, 3.25)]
    [InlineData("Warrior", 10, 3.664)]
    [InlineData("Wizard", 10, 3.25)]
    [InlineData("Hunter", 10, 4.35)]
    [InlineData("Cleric", 10, 3.25)]
    [InlineData("Bramblemaw Alpha", 4, 3.0)]
    [InlineData("Old Tuskroot", 5, 5.0)]
    [InlineData("Mother Bramble", 5, 5.0)]
    public void Carry_todays_dodge_chances_over_as_evasion(string who, int level, double todayPct)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        CombatFormula formula = context.CombatFormulas.AsNoTracking().Single();
        uint evasion;
        if (Enum.TryParse(who, out CharacterClass @class))
        {
            ClassLevelStat row = context.ClassLevelStats.AsNoTracking().AsEnumerable()
                .Single(s => s.Class == @class && s.Level == level);
            ClassStatFactors factors = context.ClassStatFactors.AsNoTracking().AsEnumerable().Single(f => f.Class == @class);
            evasion = CharacterStatsCalculator.Calculate(row, [], factors, formula).Evasion;
        }
        else
        {
            var deriver = new CreatureStatDeriver(context.CreatureBaseStats.AsNoTracking().ToList(),
                context.CreatureRarityModifiers.AsNoTracking().ToList(), NullLoggerFactory.Instance);
            CreatureTemplate template = context.CreatureTemplates.AsNoTracking().AsEnumerable().Single(t => t.Name == who);
            evasion = deriver.Derive(template, (ushort)level).Evasion;
        }

        double evadePct = HitResolver.EvasionChance(evasion, (uint)level, formula) * 100d;
        Assert.InRange(evadePct, todayPct - 0.5, todayPct + 0.5);
    }
    /// <summary>#627: the movement speed bounds, in percentage points.</summary>
    [Fact]
    public void Seed_the_movement_speed_bounds()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        CombatFormula f = Assert.Single(context.CombatFormulas.AsNoTracking().ToList());
        Assert.Equal((35f, -50f), (f.MoveSpeedCap, f.MoveSpeedFloor));
    }

    /// <summary>
    /// #1178 (B-R9): a weapon's old haste pair is folded into its attack time, so each weapon's attribute moves to its
    /// first stat and nothing follows it; the Rusted Sword, which had only the haste, carries no stat.
    /// </summary>
    [Theory]
    [InlineData(4ul, null, null)]                   // Rusted Sword
    [InlineData(7ul, StatType.Strength, 1u)]        // Bramblesteel Sword
    [InlineData(32ul, StatType.Strength, 1u)]       // Ironwood Sword
    [InlineData(6ul, StatType.Agility, 1u)]         // Briarstring Bow
    [InlineData(34ul, StatType.Agility, 1u)]        // Hunter's Shortbow
    [InlineData(8ul, StatType.Intellect, 1u)]       // Rootknot Mace
    [InlineData(35ul, StatType.Intellect, 1u)]      // Oak Mace
    [InlineData(5ul, StatType.Intellect, 1u)]       // Thornwood Staff
    [InlineData(33ul, StatType.Intellect, 1u)]      // Ash Staff
    public void Seed_each_weapons_attribute_as_its_first_stat(ulong id, StatType? stat, uint? value)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        ItemTemplate item = context.ItemTemplates.AsNoTracking().ToList().Single(i => i.Id.Value == id);
        Assert.Equal((stat, value, (StatType?)null, (uint?)null), (item.StatType1, item.StatValue1, item.StatType2, item.StatValue2));
    }

    /// <summary>#1178: a weapon's speed is its attack time, so no seeded item carries attack speed, nor movement speed.</summary>
    [Fact]
    public void Seed_attack_speed_and_movement_speed_on_no_item()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var items = context.ItemTemplates.AsNoTracking().ToList();
        Assert.DoesNotContain(items, i => StatTypesOf(i).Contains(StatType.AttackSpeed));
        Assert.DoesNotContain(items, i => StatTypesOf(i).Contains(StatType.MovementSpeed));
    }

    /// <summary>
    /// #1178 (B-R9, table C): each class's basic attack, now paced by its weapon's attack time over Agility's attack speed,
    /// keeps today's pace (800 ms over the weapon's old haste) through the stats calculator over the seeded rows, with the
    /// balance tool's starter profile (the class's starter weapon and five armour pieces) or bare-handed.
    /// </summary>
    [Theory]
    [InlineData(CharacterClass.Warrior, (ushort)1, true, 776.7, 0.1)]
    [InlineData(CharacterClass.Hunter, (ushort)1, true, 776.7, 0.1)]
    [InlineData(CharacterClass.Warrior, (ushort)1, false, 800.0, 0.1)]
    [InlineData(CharacterClass.Warrior, (ushort)10, true, 776.7, 1.5)]
    [InlineData(CharacterClass.Hunter, (ushort)10, true, 776.7, 2.0)]
    public void Carry_todays_basic_attack_pace_over(CharacterClass c, ushort level, bool starter, double todayMs,
        double tolerancePct)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        CombatFormula formula = Assert.Single(context.CombatFormulas.AsNoTracking().ToList());
        ClassStatFactors factors = context.ClassStatFactors.AsNoTracking().AsEnumerable().Single(f => f.Class == c);
        ClassLevelStat row = context.ClassLevelStats.AsNoTracking().AsEnumerable().Single(s => s.Class == c && s.Level == level);
        ulong[] starterIds = c == CharacterClass.Warrior ? [32, 36, 37, 38, 39, 40] : [34, 46, 47, 48, 49, 50];
        List<ItemTemplate> worn = starter
            ? context.ItemTemplates.AsNoTracking().AsEnumerable().Where(i => starterIds.Contains(i.Id.Value)).ToList()
            : [];
        Assert.Equal(starter ? 6 : 0, worn.Count);
        AbilityTemplate basic = context.AbilityTemplates.AsNoTracking().AsEnumerable()
            .Single(a => (a.Flags & AbilityFlags.Basic) != 0 && a.AllowedClasses!.Contains(c));

        DerivedCharacterStats stats = CharacterStatsCalculator.Calculate(row, worn, factors, formula);
        var pace = CasterPace.From(stats.Offence, stats.Weapon.AttackTimeMs / 1000f, formula);
        double paceMs = AbilityPace.Cooldown(AbilityMetadataMapper.From(basic), pace) * 1000d;

        Assert.InRange(Math.Abs(paceMs - todayMs) / todayMs * 100d, 0d, tolerancePct);
    }

    private static StatType?[] StatTypesOf(ItemTemplate i) =>
    [
        i.StatType1, i.StatType2, i.StatType3, i.StatType4, i.StatType5,
        i.StatType6, i.StatType7, i.StatType8, i.StatType9, i.StatType10,
    ];

    /// <summary>#627: every creature swings every 2.25 s, the interval the combat script used as a constant.</summary>
    [Fact]
    public void Seed_every_creature_template_to_swing_every_two_and_a_quarter_seconds()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var templates = context.CreatureTemplates.AsNoTracking().ToList();
        Assert.NotEmpty(templates);
        Assert.All(templates, t => Assert.Equal(2.25f, t.BaseAttackTime));
    }

    private static List<QuestTemplate> SeededQuests(WorldDbContext context) => context.QuestTemplates.AsNoTracking()
        .Include(q => q.Stages).Include(q => q.Objectives).ThenInclude(o => o.Drops).Include(q => q.ItemRewards)
        .Include(q => q.ItemChoices).OrderBy(q => q.Id).ToList();

    /// <summary>The storyline (#433, spec §5): three chained quests at the town's own NPCs.</summary>
    [Fact]
    public void Seed_the_forest_storyline_as_designed()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var quests = SeededQuests(context).Where(q => q.Id.Value <= 3).ToList();

        Assert.Equal([1u, 2u, 3u], quests.Select(q => q.Id.Value));
        Assert.Equal([(1ul, 1ul), (1ul, 2ul), (2ul, 2ul)], quests.Select(q => (q.GiverCreatureId.Value, q.EnderCreatureId.Value)));
        Assert.Equal(new uint?[] { null, 1, 2 }, quests.Select(q => q.RequiredQuestId?.Value));
        Assert.Equal([(ushort)1, (ushort)1, (ushort)2], quests.Select(q => q.LevelRequirement));
        Assert.Equal([(150u, 100ul), (250u, 150ul), (600u, 400ul)], quests.Select(q => (q.RewardExperience, q.RewardGold)));
        Assert.All(quests, q => Assert.Null(q.ScriptName));
        Assert.All(quests, q => Assert.Null(q.ClassRequirement));

        QuestObjective boars = Assert.Single(quests[0].Objectives);
        Assert.Equal((QuestObjectiveType.Kill, 4ul, 6u), (boars.Type, boars.CreatureTemplateId!.Value, boars.Count));

        QuestObjective tusks = Assert.Single(quests[1].Objectives);
        Assert.Equal((QuestObjectiveType.Collect, 57ul, 4u), (tusks.Type, tusks.ItemTemplateId!.Value, tusks.Count));
        QuestItemDrop drop = Assert.Single(tusks.Drops);
        Assert.Equal((4ul, 60f), (drop.CreatureTemplateId.Value, drop.Chance));
        Assert.Equal((56ul, 2u), (Assert.Single(quests[1].ItemRewards).ItemTemplateId.Value, quests[1].ItemRewards[0].Count));

        Assert.Equal([0, 1, 2], quests[2].Stages.OrderBy(s => s.Sequence).Select(s => s.Sequence));
        Assert.Equal(
            [(0, QuestObjectiveType.Kill, 5ul, 3u), (0, QuestObjectiveType.Kill, 7ul, 2u), (1, QuestObjectiveType.Talk, 11ul, 1u), (2, QuestObjectiveType.Kill, 8ul, 1u)],
            quests[2].Objectives.OrderBy(o => o.Id).Select(o => (o.StageSequence, o.Type, o.CreatureTemplateId!.Value, o.Count)));
        Assert.Equal((58ul, 1u), (Assert.Single(quests[2].ItemRewards).ItemTemplateId.Value, quests[2].ItemRewards[0].Count));
        Assert.Equal([5ul, 6ul, 7ul, 8ul], quests[2].ItemChoices.Select(c => c.ItemTemplateId.Value).Order());
        Assert.All(quests[2].ItemChoices, c => Assert.Equal(1u, c.Count));
    }

    /// <summary>Forest content pass: the second chain, 4 → 5 → 6 → 7 → 8, as the spec's table gives it.</summary>
    [Fact]
    public void Seed_the_forest_chain_as_designed()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var quests = SeededQuests(context).Where(q => q.Id.Value is >= 4 and <= 8).ToList();

        Assert.Equal([4u, 5u, 6u, 7u, 8u], quests.Select(q => q.Id.Value));
        Assert.Equal([(14ul, 14ul), (13ul, 13ul), (3ul, 11ul), (2ul, 2ul), (1ul, 1ul)],
            quests.Select(q => (q.GiverCreatureId.Value, q.EnderCreatureId.Value)));
        Assert.Equal(new uint?[] { null, 4, 5, 6, 7 }, quests.Select(q => q.RequiredQuestId?.Value));
        Assert.Equal([(ushort)2, (ushort)3, (ushort)4, (ushort)5, (ushort)7], quests.Select(q => q.LevelRequirement));
        Assert.Equal([(300u, 200ul), (400u, 250ul), (500u, 300ul), (700u, 400ul), (1500u, 1000ul)],
            quests.Select(q => (q.RewardExperience, q.RewardGold)));
        Assert.All(quests, q => Assert.Null(q.ScriptName));
        Assert.All(quests, q => Assert.Null(q.ClassRequirement));

        Assert.Equal(
            [(401u, 0, QuestObjectiveType.Collect, 59ul, 6u), (501u, 0, QuestObjectiveType.Collect, 60ul, 8u),
             (601u, 0, QuestObjectiveType.Kill, 7ul, 8u), (602u, 0, QuestObjectiveType.Collect, 61ul, 3u),
             (701u, 0, QuestObjectiveType.Collect, 62ul, 1u),
             (801u, 0, QuestObjectiveType.Talk, 12ul, 1u), (802u, 1, QuestObjectiveType.Kill, 8ul, 2u),
             (803u, 1, QuestObjectiveType.Collect, 63ul, 1u)],
            quests.SelectMany(q => q.Objectives).OrderBy(o => o.Id)
                .Select(o => (o.Id, o.StageSequence, o.Type, o.CreatureTemplateId?.Value ?? o.ItemTemplateId!.Value, o.Count)));

        Assert.Equal([(401u, 6ul, 50f), (501u, 5ul, 50f), (602u, 7ul, 35f), (701u, 9ul, 100f), (803u, 10ul, 100f)],
            quests.SelectMany(q => q.Objectives).SelectMany(o => o.Drops).OrderBy(d => d.ObjectiveId)
                .Select(d => (d.ObjectiveId, d.CreatureTemplateId.Value, d.Chance)));

        Assert.Equal([(4u, 2ul, 3u), (6u, 56ul, 2u), (7u, 64ul, 1u), (8u, 65ul, 1u)],
            quests.SelectMany(q => q.ItemRewards).OrderBy(r => r.QuestId.Value)
                .Select(r => (r.QuestId.Value, r.ItemTemplateId.Value, r.Count)));

        // Owner decision: Garrick first, then the Alphas and the Bramble Heart in one stage, so the arena counts at once.
        Assert.Equal([0, 1], quests.Single(q => q.Id.Value == 8).Stages.OrderBy(s => s.Sequence).Select(s => s.Sequence));
        Assert.All(quests.Where(q => q.Id.Value != 8), q => Assert.Single(q.Stages));
    }

    /// <summary>Quest options are offered only on an NPC's root node, so every giver and ender, and every talk target, must have one.</summary>
    [Fact]
    public void Give_every_quest_giver_ender_and_talk_target_a_dialogue_root()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var rooted = context.DialogueNodes.AsNoTracking().ToList().Where(n => n.IsRoot)
            .Select(n => n.CreatureTemplateId.Value).ToHashSet();

        foreach (QuestTemplate quest in SeededQuests(context))
        {
            Assert.Contains(quest.GiverCreatureId.Value, rooted);
            Assert.Contains(quest.EnderCreatureId.Value, rooted);
            Assert.All(quest.Objectives.Where(o => o.Type == QuestObjectiveType.Talk),
                o => Assert.Contains(o.CreatureTemplateId!.Value, rooted));
        }
    }

    [Fact]
    public void Load_every_seeded_quest_through_the_catalog_without_refusing_any()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var catalog = new QuestCatalog(SeededQuests(context), context.CreatureTemplates.AsNoTracking().ToList(),
            context.ItemTemplates.AsNoTracking().ToList(), context.DialogueNodes.AsNoTracking().ToList(), _ => null,
            NullLoggerFactory.Instance, context.DoodadTemplates.AsNoTracking().ToList());

        Assert.Empty(catalog.Refused);
        Assert.Equal(11, catalog.All.Count);
    }

    /// <summary>
    /// "The Lost Caravan" (doodads S3): Marta's quest after quest 6, in three stages. Open the caravan's strongbox
    /// (doodad 5, seen only on that step), pin its fate to the notice board (doodad 6, seen by all, used only on that
    /// step), then tell Marta. Pins the structure the doodads and the quest must agree on, not the seeded values.
    /// </summary>
    [Fact]
    public void Seed_the_lost_caravan_as_designed()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        QuestTemplate quest = SeededQuests(context).Single(q => q.Id.Value == 9);

        // Marta's sequel to quest 6: she gives it and takes it back.
        Assert.Equal((uint?)6, quest.RequiredQuestId?.Value);
        Assert.Equal(quest.GiverCreatureId, quest.EnderCreatureId);

        // One objective per stage: the strongbox, the board, then a word with the giver.
        Assert.Equal(
            [(901u, 0, QuestObjectiveType.UseDoodad, 5ul), (902u, 1, QuestObjectiveType.UseDoodad, 6ul),
             (903u, 2, QuestObjectiveType.Talk, quest.GiverCreatureId.Value)],
            quest.Objectives.OrderBy(o => o.Id).Select(o => (o.Id, o.StageSequence, o.Type,
                o.DoodadTemplateId?.Value ?? o.CreatureTemplateId!.Value)));
        Assert.Equal([0, 1, 2], quest.Stages.Select(s => s.Sequence).Order());

        // The strongbox is seen only on its step (901); the board is seen by all and used only on its step (902).
        var doodads = context.DoodadTemplates.AsNoTracking().ToList();
        DoodadTemplate strongbox = doodads.Single(d => d.Id.Value == 5), board = doodads.Single(d => d.Id.Value == 6);
        Assert.Equal(((uint?)9u, (DoodadGateCondition?)DoodadGateCondition.OnObjective, (uint?)901u, (uint?)null),
            (strongbox.VisibleQuestId, strongbox.VisibleCondition, strongbox.VisibleObjectiveId, strongbox.UsableQuestId));
        Assert.Equal(((uint?)null, (uint?)9u, (DoodadGateCondition?)DoodadGateCondition.OnObjective, (uint?)902u),
            (board.VisibleQuestId, board.UsableQuestId, board.UsableCondition, board.UsableObjectiveId));
        Assert.All([strongbox, board], d => Assert.Equal(DoodadInteraction.Click, d.Interaction));

        // Every text the quest names resolves in the base wording and in ptPT.
        AssertTextsResolveInBothLocales(context,
        [
            quest.TitleTextId.Value, quest.DescriptionTextId.Value, quest.CompletionTextId.Value,
            .. quest.Stages.Where(s => s.DescriptionTextId is not null).Select(s => s.DescriptionTextId!.Value),
            .. quest.Objectives.Select(o => o.DescriptionTextId.Value),
        ]);
    }

    /// <summary>
    /// #710: Hilde's daily, "Boar Cull": she gives it and takes it back, with no class, prerequisite, script or item
    /// reward, in one stage of one boar kill objective. Pins the structure, not the seeded values.
    /// </summary>
    [Fact]
    public void Seed_the_boar_cull_as_hildes_daily()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        QuestTemplate quest = SeededQuests(context).Single(q => q.Id.Value == 10);

        Assert.Equal((true, (QuestRepeatFrequency?)QuestRepeatFrequency.Daily), (quest.IsRepeatable, quest.RepeatFrequency));
        Assert.Equal((13ul, 13ul), (quest.GiverCreatureId.Value, quest.EnderCreatureId.Value));
        Assert.Equal(((CharacterClass?)null, (uint?)null, (string?)null),
            (quest.ClassRequirement, quest.RequiredQuestId?.Value, quest.ScriptName));
        Assert.Empty(quest.ItemRewards);
        Assert.Equal([0], quest.Stages.Select(s => s.Sequence));
        QuestObjective cull = Assert.Single(quest.Objectives);
        Assert.Equal((1001u, QuestObjectiveType.Kill, 4ul), (cull.Id, cull.Type, cull.CreatureTemplateId!.Value));

        // Every text the quest names resolves in the base wording and in ptPT.
        AssertTextsResolveInBothLocales(context,
            [quest.TitleTextId.Value, quest.DescriptionTextId.Value, quest.CompletionTextId.Value,
             cull.DescriptionTextId.Value]);
    }

    /// <summary>
    /// #711: Hilde's "Earn It Fancy" after "Pelts Before Frost": the pack and its alpha, then one of four chest pieces,
    /// one per class, and no fixed item. Pins the structure, not the seeded values.
    /// </summary>
    [Fact]
    public void Seed_earn_it_fancy_as_hildes_choice_quest()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        QuestTemplate quest = SeededQuests(context).Single(q => q.Id.Value == 11);

        Assert.Equal((13ul, 13ul), (quest.GiverCreatureId.Value, quest.EnderCreatureId.Value));
        Assert.Equal(((uint?)5u, false, (CharacterClass?)null, (string?)null),
            (quest.RequiredQuestId?.Value, quest.IsRepeatable, quest.ClassRequirement, quest.ScriptName));
        Assert.Empty(quest.ItemRewards);
        Assert.Equal([0], quest.Stages.Select(s => s.Sequence));
        Assert.Equal([(1101u, QuestObjectiveType.Kill, 5ul), (1102u, QuestObjectiveType.Kill, 8ul)],
            quest.Objectives.OrderBy(o => o.Id).Select(o => (o.Id, o.Type, o.CreatureTemplateId!.Value)));

        // One chest piece per class to choose from.
        ulong[] choices = [.. quest.ItemChoices.Select(c => c.ItemTemplateId.Value).Order()];
        Assert.Equal([13ul, 18ul, 23ul, 28ul], choices);
        Assert.All(quest.ItemChoices, c => Assert.Equal(1u, c.Count));
        Assert.Equal([CharacterClass.Warrior, CharacterClass.Wizard, CharacterClass.Hunter, CharacterClass.Cleric],
            context.ItemTemplates.AsNoTracking().ToList().Where(t => choices.Contains(t.Id.Value))
                .OrderBy(t => t.Id.Value).SelectMany(t => t.AllowedClasses));

        // Every text the quest names resolves in the base wording and in ptPT.
        AssertTextsResolveInBothLocales(context,
        [
            quest.TitleTextId.Value, quest.DescriptionTextId.Value, quest.CompletionTextId.Value,
            .. quest.Objectives.Select(o => o.DescriptionTextId.Value),
        ]);
    }

    private static void AssertTextsResolveInBothLocales(WorldDbContext context, int[] ids)
    {
        var english = context.LocalizedTexts.AsNoTracking().ToList().Select(t => t.Id.Value).ToHashSet();
        var portuguese = context.LocalizedTextLocales.AsNoTracking().ToList()
            .Where(t => t.Locale == AccountLocale.ptPT).Select(t => t.TextId.Value).ToHashSet();
        Assert.All(ids, id => Assert.Contains(id, english));
        Assert.All(ids, id => Assert.Contains(id, portuguese));
    }

    [Fact]
    public void Resolve_every_text_a_seeded_quest_names()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var texts = context.LocalizedTexts.AsNoTracking().Select(t => t.Id).ToList().Select(t => t.Value).ToHashSet();

        foreach (QuestTemplate quest in SeededQuests(context))
        {
            Assert.Contains(quest.TitleTextId.Value, texts);
            Assert.Contains(quest.DescriptionTextId.Value, texts);
            Assert.Contains(quest.CompletionTextId.Value, texts);
            Assert.All(quest.Objectives, o => Assert.Contains(o.DescriptionTextId.Value, texts));
            Assert.All(quest.Stages.Where(s => s.DescriptionTextId is not null), s => Assert.Contains(s.DescriptionTextId!.Value, texts));
        }
    }

    [Fact]
    public void Seed_the_boar_tusk_as_a_quest_item_that_drops_nowhere_else_and_is_never_sold()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        ItemTemplate tusk = context.ItemTemplates.AsNoTracking().ToList().Single(i => i.Id.Value == 57);

        Assert.Equal("Boar Tusk", tusk.Name);
        Assert.Equal((ItemClass.Quest, ItemSubClass.QuestItem), (tusk.Class, tusk.SubClass));
        Assert.True(tusk.Flags.HasFlag(ItemTemplateFlags.QuestItem));
        Assert.True(tusk.Flags.HasFlag(ItemTemplateFlags.NoSell));
        Assert.DoesNotContain(context.LootTableEntries.AsNoTracking().ToList(), e => e.ItemTemplateId?.Value == 57);
        Assert.DoesNotContain(context.VendorStocks.AsNoTracking().ToList(), s => s.ItemTemplateId.Value == 57);
    }

    [Fact]
    public void Seed_the_alphas_fang_pendant_as_an_uncommon_neck_piece_for_every_class()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        ItemTemplate pendant = context.ItemTemplates.AsNoTracking().ToList().Single(i => i.Id.Value == 58);

        Assert.Equal("Alpha's Fang Pendant", pendant.Name);
        Assert.Equal((ItemClass.Armor, ItemSubClass.Amulet, ItemSlotType.Neck, ItemRarity.Uncommon),
            (pendant.Class, pendant.SubClass, pendant.Slot, pendant.Rarity));
        Assert.Empty(pendant.AllowedClasses);
        Assert.False(pendant.Flags.HasFlag(ItemTemplateFlags.Unique));
    }

    /// <summary>Forest content pass, quests 4-8: five quest items that drop only for characters collecting them and are never sold.</summary>
    [Theory]
    [InlineData(59ul, "Blightfly Wing")]
    [InlineData(60ul, "Fen Wolf Pelt")]
    [InlineData(61ul, "Ledger Page")]
    [InlineData(62ul, "Tuskroot Heartwood")]
    [InlineData(63ul, "Bramble Heart")]
    public void Seed_the_chains_quest_items_as_never_sold_and_never_looted(ulong id, string name)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        ItemTemplate item = context.ItemTemplates.AsNoTracking().ToList().Single(i => i.Id.Value == id);

        Assert.Equal(name, item.Name);
        Assert.Equal((ItemClass.Quest, ItemSubClass.QuestItem), (item.Class, item.SubClass));
        Assert.True(item.Flags.HasFlag(ItemTemplateFlags.QuestItem));
        Assert.True(item.Flags.HasFlag(ItemTemplateFlags.NoSell));
        Assert.Equal(20u, item.MaxStackSize);
        Assert.Equal((0u, 0u), (item.BuyPrice, item.SellPrice));
        Assert.DoesNotContain(context.LootTableEntries.AsNoTracking().ToList(), e => e.ItemTemplateId?.Value == id);
        Assert.DoesNotContain(context.VendorStocks.AsNoTracking().ToList(), s => s.ItemTemplateId.Value == id);
    }

    /// <summary>The chain's two rewards: rings for every class, in a finger slot, never Unique (a reward must always fit).</summary>
    [Theory]
    [InlineData(64ul, "Heartwood Band", ItemRarity.Uncommon, (ushort)5, 3u, 2u)]
    [InlineData(65ul, "Thornheart Signet", ItemRarity.Rare, (ushort)7, 5u, 4u)]
    public void Seed_the_chains_rings_for_every_class(ulong id, string name, ItemRarity rarity, ushort level, uint stamina, uint armor)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        ItemTemplate ring = context.ItemTemplates.AsNoTracking().ToList().Single(i => i.Id.Value == id);

        Assert.Equal(name, ring.Name);
        Assert.Equal((ItemClass.Armor, ItemSubClass.Ring, ItemSlotType.Finger, rarity), (ring.Class, ring.SubClass, ring.Slot, ring.Rarity));
        Assert.Equal(level, ring.RequiredLevel);
        Assert.Empty(ring.AllowedClasses);
        Assert.Equal((StatType.Stamina, stamina), (ring.StatType1, ring.StatValue1));
        Assert.Equal((StatType.Armor, armor), (ring.StatType2, ring.StatValue2));
        Assert.False(ring.Flags.HasFlag(ItemTemplateFlags.Unique));
        Assert.Equal(1u, ring.MaxStackSize);
    }

    /// <summary>An item for every class lists none (#1171): a list names classes only to restrict the item to some.</summary>
    [Fact]
    public void Seed_no_item_that_lists_every_class()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        Assert.DoesNotContain(context.ItemTemplates.AsNoTracking().ToList(),
            i => CharacterClasses.Playable.All(i.AllowedClasses.Contains));
    }

    [Fact]
    public void Gate_no_vendor_row_behind_a_quest_yet()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        Assert.All(context.VendorStocks.AsNoTracking().ToList(), s => Assert.Null(s.RequiredQuestId));
    }

    /// <summary>Item use: the potions and the Town Portal Scroll are the seeded usable items.</summary>
    [Theory]
    [InlineData(1ul, "RestoreHealth", null, 30000u, "potion", 30u)]
    [InlineData(2ul, "RestorePower", null, 30000u, "potion", 30u)]
    [InlineData(3ul, "TownPortalScroll", 3000u, 30000u, null, null)]
    [InlineData(56ul, "RestoreHealth", null, 30000u, "potion", 60u)]
    public void Give_the_usable_items_their_use_data(
        ulong id, string script, uint? castMs, uint? cooldownMs, string? group, uint? value)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        ItemTemplate item = context.ItemTemplates.AsNoTracking().ToList().Single(i => i.Id.Value == id);

        Assert.Equal((script, castMs, cooldownMs, group, value),
            (item.UseScript, item.UseCastTimeMs, item.UseCooldownMs, item.UseCooldownGroup, item.UseValue));
    }

    /// <summary>The forest scrolls (9-11) stay inert, and so does every other item: only the four above are usable.</summary>
    [Fact]
    public void Leave_every_other_item_without_a_use_script()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        ulong[] usable = context.ItemTemplates.AsNoTracking().ToList()
            .Where(i => i.UseScript is not null).Select(i => i.Id.Value).Order().ToArray();

        Assert.Equal([1ul, 2ul, 3ul, 56ul], usable);
        Assert.All(context.ItemTemplates.AsNoTracking().ToList().Where(i => i.UseScript is null), i =>
            Assert.Equal(((uint?)null, (uint?)null, (string?)null, (uint?)null),
                (i.UseCastTimeMs, i.UseCooldownMs, i.UseCooldownGroup, i.UseValue)));
    }

    /// <summary>
    /// Every seeded doodad's model kit, model keys and clips are plain asset names (the game client's rule): a bad one
    /// fails here, before the catalog refuses the template at load and the client draws nothing.
    /// </summary>
    [Fact]
    public void Seed_every_doodad_model_kit_key_and_clip_as_a_plain_asset_name()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        Assert.All(context.DoodadTemplates.AsNoTracking().ToList(), t =>
        {
            Assert.True(AssetNames.IsPlain(t.ModelKit), $"doodad {t.Id.Value} model kit '{t.ModelKit}'");
            Assert.True(AssetNames.IsPlain(t.ModelKey), $"doodad {t.Id.Value} model key '{t.ModelKey}'");
            Assert.All(new[] { t.UsedModelKey, t.ReadyClip, t.UseClip, t.UsedClip }.Where(n => n is not null),
                n => Assert.True(AssetNames.IsPlain(n), $"doodad {t.Id.Value} asset '{n}'"));
        });
    }

    /// <summary>The forest's doodads (doodads S2): the chest, the barrel, the shrine and the fountain, every column.</summary>
    [Theory]
    [InlineData(1u, "Forest Chest", "props-kit", "Chest_Wood", null, "Chest_Closed", "Chest_Open", "Chest_Opened", 0u, 5f, 0f,
        DoodadUseLimit.OncePerInstance, DoodadSpentLook.ShowUsed, nameof(LootDoodadScript), 12, 50, 150, 0u, DoodadCollision.None, "")]
    [InlineData(2u, "Old Barrel", "props-kit", "Barrel", "avalon_barrel_broken", null, null, null, 0u, 5f, 0f,
        DoodadUseLimit.OncePerInstance, DoodadSpentLook.ShowUsed, nameof(LootDoodadScript), 13, 0, 20, 0u, DoodadCollision.None, "")]
    [InlineData(3u, "Wayside Shrine", "town-kit", "avalon_shrine", "avalon_shrine_spent", null, null, null, 0u, 5f, 10f,
        DoodadUseLimit.OncePerInstance, DoodadSpentLook.ShowUsed, nameof(ShrineDoodadScript), 0, 0, 0, 0u, DoodadCollision.Box,
        "8:40 9:40 10:20")]
    [InlineData(4u, "Mossy Fountain", "town-kit", "avalon_forest_fountain", "avalon_forest_fountain_dry", null, null, null, 2000u,
        5f, 10f, DoodadUseLimit.OncePerInstance, DoodadSpentLook.ShowUsed, nameof(FountainDoodadScript), 0, 0, 0, 50u,
        DoodadCollision.Box, "")]
    public void Seed_each_forest_doodad_as_designed(uint id, string name, string kit, string model, string? usedModel,
        string? readyClip, string? useClip, string? usedClip, uint timeMs, float range, float radius, DoodadUseLimit limit,
        DoodadSpentLook look, string script, int lootTable, int minGold, int maxGold, uint restorePct,
        DoodadCollision collision, string outcomes)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        DoodadTemplate t = context.DoodadTemplates.AsNoTracking().Include(d => d.AuraOutcomes).AsEnumerable()
            .Single(d => d.Id.Value == id);

        Assert.Equal((name, kit, model, usedModel, readyClip, useClip, usedClip),
            (t.Name, t.ModelKit, t.ModelKey, t.UsedModelKey, t.ReadyClip, t.UseClip, t.UsedClip));
        Assert.Equal((DoodadInteraction.Click, timeMs, range, radius, limit, look, (uint?)null, script),
            (t.Interaction, t.InteractionTimeMs, t.InteractionRange, t.AreaRadius, t.UseLimit, t.SpentLook, t.RespawnSeconds,
                t.ScriptName));
        Assert.Equal((lootTable == 0 ? null : lootTable, minGold, maxGold, restorePct, collision),
            (t.LootTableId?.Value, t.MinGold, t.MaxGold, t.RestorePct, t.Collision));
        Assert.Equal(outcomes, string.Join(' ', t.AuraOutcomes.OrderBy(o => o.AuraId.Value).Select(o => $"{o.AuraId.Value}:{o.Weight}")));
        // No forest doodad is gated: a chest, a barrel, a shrine or a fountain is for everyone.
        Assert.Equal(((uint?)null, (uint?)null), (t.VisibleQuestId, t.UsableQuestId));
    }

    /// <summary>The seeded doodads are content the catalog refuses at load if they are wrong, so load them as the world does.</summary>
    [Fact]
    public void Load_every_seeded_doodad_through_the_catalog_without_refusing_any()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var scripts = new ScriptManager(NullLoggerFactory.Instance);
        scripts.Load();

        var catalog = new DoodadCatalog(
            context.DoodadTemplates.AsNoTracking().Include(d => d.AuraOutcomes).ToList(),
            new LootCatalog(context.LootTables.AsNoTracking().Include(t => t.Entries).ToList(), NullLoggerFactory.Instance),
            context.AuraTemplates.AsNoTracking().Include(a => a.Modifiers).ToList(),
            SeededQuests(context), scripts.GetDoodadScript, NullLoggerFactory.Instance);

        Assert.Empty(catalog.Refused);
        Assert.Equal([1u, 2u, 3u, 4u, 5u, 6u], catalog.Templates.Select(t => t.Id.Value));
        // The forest set runs its scripts; the caravan's strongbox and notice board need none, as a use only credits the quest.
        Assert.All(catalog.Templates, t => Assert.Equal(t.Id.Value <= 4, t.ScriptType is not null));
    }
}
