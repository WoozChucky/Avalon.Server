using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.Utils;
using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Database.World.Repositories;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.Abilities;
using Avalon.World.Auras;
using Avalon.World.Doodads;
using Avalon.World.Doodads.Scripts;
using Avalon.World.Loot;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;
using Avalon.World.Quests;
using Avalon.World.Reload;
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
public class SeedIntegrityShould(SeedIntegrityShould.BaseStatTables baseStatTables)
    : IClassFixture<SeedIntegrityShould.BaseStatTables>
{
    /// <summary>
    /// The seeded bases' base-stat ranges (#1179), read once for the class: the base theories check a template's drafted
    /// base stats against them row by row.
    /// </summary>
    public sealed class BaseStatTables
    {
        public BaseStatTables()
        {
            using var database = SqliteDatabase.World();
            using WorldDbContext context = database.CreateDbContext();
            Ranges = context.ItemTemplateBaseStats.AsNoTracking().ToList().ToLookup(s => s.TemplateId.Value);
        }

        public ILookup<ulong, ItemTemplateBaseStat> Ranges { get; }
    }
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

    private static readonly LootTableId s_forestJewellery = new(14);

    private static readonly LootTableId s_forestBossGear = new(15);

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
        Assert.Equal(15, catalog.TableCount);
        // Table 1: 2. Tables 2-8: 7 each, and the uniques (#1179): one in table 6, two in table 7, and two with the boss
        // gear's reference in table 8. Table 9: 6 weapon and off-hand bases. Table 10: 3 scrolls. Table 11: 18 armour
        // bases. Table 12, the forest chest: 7. Table 13, the forest barrel: 2. Table 14, the forest jewellery: 2.
        // Table 15, the forest boss gear: 26.
        Assert.Equal(2 + (7 * 7) + 1 + 2 + 3 + 6 + 3 + 18 + 7 + 2 + 2 + 26, catalog.EntryCount);
        Assert.All(context.CreatureTemplates.AsNoTracking().ToList().Where(t => t.LootTableId is not null),
            t => Assert.True(catalog.TryGet(t.LootTableId!, out _), $"{t.Name} names a table the catalog does not hold"));
    }

    /// <summary>
    /// Every creature table rolls its own potions, then the shared pools: the common table, the
    /// weapon and scroll groups (each drops exactly one entry when it rolls, so it is referenced at
    /// the chance it should drop with), and the armour and jewellery tables, rolled every kill.
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
            Assert.Single(table.Entries, e => e.ReferenceTableId == s_forestJewellery && e.Chance == 100f);
        });
    }

    /// <summary>#1179: one tier-1 base of each weapon and off-hand kind, one group at equal weights.</summary>
    [Fact]
    public void Seed_the_forest_weapons_as_one_group_of_six_bases()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var items = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(i => i.Id);
        LootTable table = LoadTable(context, s_forestWeapons);

        Assert.Single(table.Entries.Select(e => e.Chance).Distinct());
        Assert.All(table.Entries, e =>
        {
            Assert.Equal(1, e.GroupId);
            ItemTemplate item = items[e.ItemTemplateId!];
            Assert.Equal((ItemRarity.Common, (ushort?)1), (item.Rarity, item.RequiredLevel));
        });
        Assert.Equal(
            [ItemBaseKind.Sword, ItemBaseKind.Mace, ItemBaseKind.Staff, ItemBaseKind.Bow, ItemBaseKind.Shield, ItemBaseKind.CasterOffHand],
            table.Entries.OrderBy(e => e.Sequence).Select(e => items[e.ItemTemplateId!].BaseKind));
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

    /// <summary>
    /// #1179: every piece rolls on its own at 2 %, and the table holds each of the eighteen armour kinds (three families
    /// in six slots) exactly once, at tier 1, for every class.
    /// </summary>
    [Fact]
    public void Seed_the_forest_armour_as_eighteen_independent_tier_one_pieces()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var items = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(i => i.Id);
        LootTable table = LoadTable(context, s_forestArmour);

        Assert.All(table.Entries, e =>
        {
            Assert.Null(e.GroupId);
            Assert.Equal(2f, e.Chance);
            ItemTemplate piece = items[e.ItemTemplateId!];
            Assert.Equal((ItemClass.Armor, ItemRarity.Common, (ushort?)1), (piece.Class, piece.Rarity, piece.RequiredLevel));
            Assert.Empty(piece.AllowedClasses);
        });
        Assert.Equal(
            Enumerable.Range((int)ItemBaseKind.PlateHead, 18).Select(k => (ItemBaseKind)k),
            table.Entries.Select(e => items[e.ItemTemplateId!].BaseKind!.Value).Order());
    }

    /// <summary>
    /// Seeded kills, rolled the way the tick rolls them: one gold pile, at most one weapon and at most
    /// one scroll (each is a group, referenced once; a boss kill's gear, #1179, may add a second weapon), and nothing
    /// names an item the seed does not have. Over 1000 kills of each creature, the 2 % weapon, the 10 % scroll and the
    /// 2 % armour and jewellery pieces all turn up.
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
        HashSet<ulong> jewellery = ItemsIn(tables, s_forestJewellery);
        var catalog = new LootCatalog(tables, NullLoggerFactory.Instance);
        var roller = new LootRoller(new LootRandom(new Random(460)), NullLogger<LootRoller>.Instance);

        var killable = context.CreatureTemplates.AsNoTracking().ToList()
            .Where(t => !t.Invulnerable).ToList();
        Assert.NotEmpty(killable);

        foreach (CreatureTemplate template in killable)
        {
            int weaponsDropped = 0, scrollsDropped = 0, armourDropped = 0, jewelleryDropped = 0;
            bool bossGear = tables.Single(t => t.Id == template.LootTableId).Entries.Any(e => e.ReferenceTableId == s_forestBossGear);

            for (int i = 0; i < 1000; i++)
            {
                IReadOnlyList<RolledDrop> drops = roller.Roll(template, catalog, items);
                var dropped = drops.Where(d => !d.IsGold).Select(d => d.ItemTemplateId!.Value).ToList();

                int weaponCount = dropped.Count(weapons.Contains);
                int scrollCount = dropped.Count(scrolls.Contains);
                Assert.InRange(weaponCount, 0, bossGear ? 2 : 1);
                Assert.InRange(scrollCount, 0, 1);
                weaponsDropped += weaponCount;
                scrollsDropped += scrollCount;
                armourDropped += dropped.Count(armour.Contains);
                jewelleryDropped += dropped.Count(jewellery.Contains);

                Assert.Single(drops, d => d.IsGold);
                Assert.All(dropped, id => Assert.Contains(items, t => t.Id.Value == id));
            }

            Assert.True(weaponsDropped > 0, $"{template.Name} dropped no weapon in 1000 kills");
            Assert.True(scrollsDropped > 0, $"{template.Name} dropped no scroll in 1000 kills");
            Assert.True(armourDropped > 0, $"{template.Name} dropped no armour in 1000 kills");
            Assert.True(jewelleryDropped > 0, $"{template.Name} dropped no jewellery in 1000 kills");
        }
    }

    /// <summary>Owner decision: forest drops can be sold. Every item in the weapon, scroll, armour and jewellery pools.</summary>
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
            .Concat(ItemsIn(tables, s_forestJewellery))
            .ToHashSet();

        Assert.Equal(6 + 3 + 18 + 2, pool.Count);
        Assert.All(items.Where(i => pool.Contains(i.Id.Value)), item =>
        {
            Assert.False(item.Flags.HasFlag(ItemTemplateFlags.NoSell), $"{item.Name} is marked NoSell");
            Assert.True(item.SellPrice > 0, $"{item.Name} sells for nothing");
        });
    }

    /// <summary>
    /// The forest's doodad tables: the chest rolls potions, then the creatures' shared pools (the common table, the armour
    /// table and the jewellery table every time, the weapon group at 10 % and the scroll group at 25 %, richer than a
    /// kill); the barrel rolls a potion or two and nothing else.
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
             (4, null, 9, 10f, 1, 1, null), (5, null, 10, 25f, 1, 1, null), (6, null, 11, 100f, 1, 1, null),
             (7, null, 14, 100f, 1, 1, null)],
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

    /// <summary>
    /// Every pool weapon goes in the main hand with a weapon sub-class, and the bow is the ranged one; the shield and the
    /// caster off-hand (#1179) are armour worn in the off hand.
    /// </summary>
    [Fact]
    public void Seed_Every_Pool_Weapon_For_The_Main_Hand_With_A_Weapon_Sub_Class()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var items = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(i => i.Id);
        var pool = LoadTable(context, s_forestWeapons).Entries.Select(e => items[e.ItemTemplateId!]).ToList();

        Assert.All(pool.Where(w => w.Class == ItemClass.Weapon), w =>
        {
            Assert.Equal(ItemSlotType.MainHand, w.Slot);
            Assert.Contains(w.SubClass, new[] { ItemSubClass.OneHanded, ItemSubClass.TwoHanded, ItemSubClass.Ranged });
        });
        Assert.Equal(ItemSubClass.Ranged, Assert.Single(pool, w => w.BaseKind == ItemBaseKind.Bow).SubClass);
        Assert.Equal(
            [(ItemClass.Armor, ItemSubClass.Shield, (ItemSlotType?)ItemSlotType.OffHand),
             (ItemClass.Armor, ItemSubClass.CasterOffHand, (ItemSlotType?)ItemSlotType.OffHand)],
            pool.Where(w => w.Class != ItemClass.Weapon).OrderBy(w => w.BaseKind).Select(w => (w.Class, w.SubClass, w.Slot)));
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

    /// <summary>
    /// #1178: every class gains the formula's one life per Stamina, so each class's BaseHp is rebalanced to keep a
    /// character's health with nothing worn exactly what its class's own rate gave: 10, 5, 8 and 7 life per Stamina over
    /// a BaseHp of 20, 16, 18 and 18 per level for the Warrior, Wizard, Hunter and Cleric.
    /// </summary>
    [Fact]
    public void Carry_todays_bare_health_over_for_every_class_and_level()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var factors = context.ClassStatFactors.AsNoTracking().ToList().ToDictionary(f => f.Class);
        CombatFormula formula = context.CombatFormulas.AsNoTracking().Single();
        var old = new Dictionary<CharacterClass, (uint HpPerLevel, uint PerStamina)>
        {
            [CharacterClass.Warrior] = (20, 10),
            [CharacterClass.Wizard] = (16, 5),
            [CharacterClass.Hunter] = (18, 8),
            [CharacterClass.Cleric] = (18, 7),
        };

        foreach (ClassLevelStat row in context.ClassLevelStats.AsNoTracking().ToList())
        {
            (uint hpPerLevel, uint perStamina) = old[row.Class];
            uint bare = CharacterStatsCalculator.Calculate(row, [], factors[row.Class], formula).MaxHealth;
            Assert.True(hpPerLevel * row.Level + row.Stamina * perStamina == bare,
                $"{row.Class} level {row.Level}: bare health {bare}, was {hpPerLevel * row.Level + row.Stamina * perStamina}");
        }
    }

    private static LootTable LoadTable(WorldDbContext context, LootTableId id) =>
        Assert.Single(context.LootTables.AsNoTracking().Include(t => t.Entries).ToList(), t => t.Id == id);

    private static HashSet<ulong> ItemsIn(IEnumerable<LootTable> tables, LootTableId id) =>
        tables.Single(t => t.Id == id).Entries.Where(e => e.ItemTemplateId is not null)
            .Select(e => e.ItemTemplateId!.Value).ToHashSet();

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
    /// and the speeds chosen for the projectiles. Every one is creature-only (no class) and free, and deals its
    /// creature's natural range times its DamageEffectiveness (#1178: the old weapon coefficient as a percentage, the last
    /// column here); a basic (100 %) lists the seeded 2.25 s swing interval, which is what it waits.
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

        // #1178: the old weapon coefficient is the damage effectiveness, as a percentage.
        Assert.Equal((name, shape, reach, radius, arc, speed, pierce, castTime, cooldown, MathF.Round(baseDamage * 100f)),
            (a.Name, a.Shape, a.Reach, a.Radius, a.ArcDegrees, a.ProjectileSpeed, a.Pierce, a.CastTime, a.Cooldown,
                a.DamageEffectiveness));
        Assert.Empty(a.AllowedClasses);
        Assert.Equal((0u, 0), (a.Cost, a.PowerGainPerHit));
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
        Assert.Equal(100f, context.AbilityTemplates.AsNoTracking().AsEnumerable()
            .Single(a => a.Id == kit.Basic).DamageEffectiveness);
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

    /// <summary>
    /// Auras: the seeded auras, each as designed (8-10 the forest shrine's outcomes, 12-15 the crowd control of #770,
    /// with the design's break chances in percent; the slows Crippled and Hexed take its slow row).
    /// </summary>
    [Theory]
    [InlineData(1u, "Bleed", AuraKind.Harmful, 12000u, 3000u, AuraPeriodicKind.Damage, 12f, AuraStacking.Stack, 3u, CrowdControl.None, 0f, 0f)]
    [InlineData(2u, "Burn", AuraKind.Harmful, 9000u, 3000u, AuraPeriodicKind.Damage, 0f, AuraStacking.Refresh, 1u, CrowdControl.None, 0f, 0f)]
    [InlineData(3u, "Crippled", AuraKind.Harmful, 6000u, 0u, AuraPeriodicKind.None, 0f, AuraStacking.Refresh, 1u, CrowdControl.None, 0f, 0.5f)]
    [InlineData(4u, "Renew", AuraKind.Helpful, 12000u, 3000u, AuraPeriodicKind.Heal, 0f, AuraStacking.Refresh, 1u, CrowdControl.None, 0f, 0f)]
    [InlineData(5u, "Fortified", AuraKind.Helpful, 30000u, 0u, AuraPeriodicKind.None, 0f, AuraStacking.Refresh, 1u, CrowdControl.None, 0f, 0f)]
    [InlineData(6u, "Poison", AuraKind.Harmful, 9000u, 3000u, AuraPeriodicKind.Damage, 3f, AuraStacking.Stack, 3u, CrowdControl.None, 0f, 0f)]
    [InlineData(7u, "Sundered", AuraKind.Harmful, 10000u, 0u, AuraPeriodicKind.None, 0f, AuraStacking.Refresh, 1u, CrowdControl.None, 0f, 0f)]
    [InlineData(8u, "Blessed", AuraKind.Helpful, 120000u, 0u, AuraPeriodicKind.None, 0f, AuraStacking.Refresh, 1u, CrowdControl.None, 0f, 0f)]
    [InlineData(9u, "Emboldened", AuraKind.Helpful, 120000u, 0u, AuraPeriodicKind.None, 0f, AuraStacking.Refresh, 1u, CrowdControl.None, 0f, 0f)]
    [InlineData(10u, "Hexed", AuraKind.Harmful, 60000u, 0u, AuraPeriodicKind.None, 0f, AuraStacking.Refresh, 1u, CrowdControl.None, 0f, 0.5f)]
    [InlineData(12u, "Stunned", AuraKind.Harmful, 1500u, 0u, AuraPeriodicKind.None, 0f, AuraStacking.Refresh, 1u, CrowdControl.Stun, 10f, 0.5f)]
    [InlineData(13u, "Bound", AuraKind.Harmful, 3000u, 0u, AuraPeriodicKind.None, 0f, AuraStacking.Refresh, 1u, CrowdControl.Root, 25f, 0.5f)]
    [InlineData(14u, "Frozen", AuraKind.Harmful, 2500u, 0u, AuraPeriodicKind.None, 0f, AuraStacking.Refresh, 1u, CrowdControl.Root, 25f, 0.5f)]
    [InlineData(15u, "Silenced", AuraKind.Harmful, 3000u, 0u, AuraPeriodicKind.None, 0f, AuraStacking.Refresh, 1u, CrowdControl.Silence, 0f, 0f)]
    public void Seed_each_aura_as_designed(uint id, string name, AuraKind kind, uint duration, uint interval,
        AuraPeriodicKind periodic, float periodicBase, AuraStacking stacking, uint maxStacks, CrowdControl control, float breakOnDamage, float breakPerSecond)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        AuraTemplate a = context.AuraTemplates.AsNoTracking().AsEnumerable().Single(t => t.Id.Value == id);

        Assert.Equal((name, name.ToLowerInvariant(), kind, duration, interval, periodic, periodicBase, stacking, maxStacks,
                (string?)null),
            (a.Name, a.Icon, a.Kind, a.DurationMs, a.TickIntervalMs, a.PeriodicKind, a.PeriodicBase, a.Stacking, a.MaxStacks,
                a.ScriptName));
        Assert.Equal((control, breakOnDamage, breakPerSecond), (a.Control, a.BreakOnDamagePct, a.BreakPerSecondPct));
    }

    [Fact]
    public void Seed_exactly_eight_aura_modifiers()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        Assert.Equal(
            [(3u, AuraStat.MovementSpeed, AuraModifierKind.Flat, -30f), (5u, AuraStat.Armor, AuraModifierKind.Increased, 20f),
             (7u, AuraStat.Armor, AuraModifierKind.Increased, -25f), (8u, AuraStat.MaxHealth, AuraModifierKind.Increased, 10f),
             (9u, AuraStat.IncreasedDamage, AuraModifierKind.Flat, 10f), (9u, AuraStat.IncreasedHealing, AuraModifierKind.Flat, 10f),
             (10u, AuraStat.Armor, AuraModifierKind.Increased, -10f), (10u, AuraStat.MovementSpeed, AuraModifierKind.Flat, -15f)],
            context.AuraStatModifiers.AsNoTracking().AsEnumerable().OrderBy(m => m.AuraId.Value).ThenBy(m => m.Stat)
                .Select(m => (m.AuraId.Value, m.Stat, m.Kind, m.Value)));
    }

    /// <summary>
    /// Auras: the aura abilities, each with its shape, timing, cost, effects and aura; 204-235 the crowd control
    /// of #770, which a dodge or a miss lands no control from: Hush (235) deals nothing and is pure silence (#1164).
    /// </summary>
    [Theory]
    [InlineData(203u, "Rend", AbilityShape.Cone, AbilityAimMode.Movement, 2.5f, 0f, 90f, 0f, 6000u, 10u, PowerType.Fury, SpellEffect.Damage, 1u)]
    [InlineData(213u, "Ignite", AbilityShape.Circle, AbilityAimMode.Cursor, 18f, 3f, 0f, 0f, 6000u, 20u, PowerType.Mana, SpellEffect.Debuff, 2u)]
    [InlineData(223u, "Crippling Shot", AbilityShape.Projectile, AbilityAimMode.Cursor, 25f, 0f, 0f, 14f, 8000u, 15u, PowerType.Energy, SpellEffect.Damage, 3u)]
    [InlineData(233u, "Renew", AbilityShape.Circle, AbilityAimMode.Cursor, 15f, 4f, 0f, 0f, 6000u, 15u, PowerType.Mana, SpellEffect.Buff, 4u)]
    [InlineData(234u, "Fortify", AbilityShape.Circle, AbilityAimMode.Movement, 0f, 8f, 0f, 0f, 20000u, 20u, PowerType.Mana, SpellEffect.Buff, 5u)]
    [InlineData(204u, "Shield Bash", AbilityShape.Cone, AbilityAimMode.Movement, 2.5f, 0f, 90f, 0f, 15000u, 15u, PowerType.Fury, SpellEffect.Damage, 12u)]
    [InlineData(214u, "Frost Bind", AbilityShape.Circle, AbilityAimMode.Cursor, 18f, 3f, 0f, 0f, 20000u, 25u, PowerType.Mana, SpellEffect.Damage, 14u)]
    [InlineData(224u, "Binding Shot", AbilityShape.Projectile, AbilityAimMode.Cursor, 25f, 0f, 0f, 14f, 15000u, 20u, PowerType.Energy, SpellEffect.Damage, 13u)]
    [InlineData(235u, "Hush", AbilityShape.Projectile, AbilityAimMode.Cursor, 18f, 0f, 0f, 17f, 20000u, 20u, PowerType.Mana, SpellEffect.Debuff, 15u)]
    [InlineData(317u, "Venom Spit", AbilityShape.Projectile, AbilityAimMode.Cursor, 10f, 0f, 0f, 14f, 8000u, 0u, PowerType.None, SpellEffect.Damage, 6u)]
    [InlineData(318u, "Sundering Howl", AbilityShape.Circle, AbilityAimMode.Movement, 0f, 6f, 0f, 0f, 18000u, 0u, PowerType.None, SpellEffect.Debuff, 7u)]
    public void Seed_each_aura_ability_as_designed(uint id, string name, AbilityShape shape, AbilityAimMode aim, float reach,
        float radius, float arc, float speed, uint cooldown, uint cost, PowerType pool, SpellEffect effects, uint aura)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        AbilityTemplate a = context.AbilityTemplates.AsNoTracking().AsEnumerable().Single(t => t.Id.Value == id);

        Assert.Equal((name, shape, aim, reach, radius, arc, speed, 0u, cooldown, cost, pool, effects, (uint?)aura),
            (a.Name, a.Shape, a.AimMode, a.Reach, a.Radius, a.ArcDegrees, a.ProjectileSpeed, a.CastTime, a.Cooldown, a.Cost,
                a.CostPowerType, a.Effects, a.AuraId?.Value));
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
    /// resistance bounds and the class-neutral attribute conversions; no class has a base dodge, block, crit or life
    /// rate of its own any more.
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
        Assert.Equal((0d, 0d, (long?)100L), (w.PowerPerIntellect, w.PowerPerAgility, w.FixedPower));
        ClassStatFactors h = factors[CharacterClass.Hunter];
        Assert.Equal((2d, 0.8d, (long?)null), (h.PowerPerIntellect, h.PowerPerAgility, h.FixedPower));
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
    /// #1179: a base carries no attribute and no Stamina: its attribute is a requirement, and attributes come from the
    /// character and from rolled modifiers.
    /// </summary>
    [Fact]
    public void Seed_each_weapons_attribute_as_a_requirement_and_no_attribute_line_on_any_base()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var bases = context.ItemTemplates.AsNoTracking().ToList()
            .Where(i => i.Rarity == ItemRarity.Common && i.BaseKind is not null).ToList();

        Assert.Equal(122, bases.Count);
        StatType?[] attributes = [StatType.Strength, StatType.Agility, StatType.Intellect, StatType.Stamina];
        Assert.All(bases, b => Assert.DoesNotContain(StatTypesOf(b), attributes.Contains));
        Assert.All(bases.Where(b => b.Class == ItemClass.Weapon && b.RequiredLevel > 1), w =>
            Assert.True(w.RequiredStrength is not null || w.RequiredAgility is not null || w.RequiredIntellect is not null,
                $"{w.Name} asks no attribute"));
    }

    /// <summary>
    /// #1178: a weapon's speed is its attack time, so no base carries attack speed; #1179: a unique's stats come from its
    /// rolled modifier, never its template. No item carries movement speed.
    /// </summary>
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
    /// #1178 (table C): each class's basic attack, now paced by its weapon's attack time over Agility's attack speed,
    /// keeps today's pace (800 ms over the weapon's old haste) through the stats calculator over the seeded rows, with the
    /// balance tool's starter profile (the class's starter weapon and five armour pieces, as they stood before #1179:
    /// <see cref="RetiredGearProfiles" />) or bare-handed.
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
        List<WornItem> worn = starter ? RetiredGearProfiles.For("starter", c) : [];
        AbilityTemplate basic = context.AbilityTemplates.AsNoTracking().AsEnumerable()
            .Single(a => (a.Flags & AbilityFlags.Basic) != 0 && a.AllowedClasses!.Contains(c));

        DerivedCharacterStats stats = CharacterStatsCalculator.Calculate(row, worn, factors, formula);
        var pace = CasterPace.From(stats.Offence, stats.Weapon.AttackTimeMs / 1000f, formula);
        double paceMs = AbilityPace.Cooldown(AbilityMetadataMapper.From(basic), pace) * 1000d;

        Assert.InRange(Math.Abs(paceMs - todayMs) / todayMs * 100d, 0d, tolerancePct);
    }

    /// <summary>
    /// #1178 (R17, tables A and B): each kit ability carries today's numbers over, through the stats calculator over the
    /// seeded row and the balance tool's starter profile (the class's starter weapon and five armour pieces, as they stood
    /// before #1179: <see cref="RetiredGearProfiles" />). The average of
    /// the ability's base with every roll at its low and at its high end (<see cref="RollEnds" />): an Attack's or a Spell's
    /// <see cref="OffenceMath.DirectBase" />, a Heal's <see cref="OffenceMath.HealBase" />, or, through its aura, the
    /// whole of <see cref="OffenceMath.PeriodicTotal" /> (Rend's Bleed, Ignite's Burn, Renew). Today's average is the old
    /// <c>EffectValue + coefficient × damage stat + weapon coefficient × roll</c>, the tolerance its table's. The re-expressed
    /// weapons (owner decision D2) carry their tiers over too: the balance tool's forest profile (the Bramblesteel Sword
    /// and the Briarstring Bow, 7-11 then, 5-7 now) and the starter Warrior with the Rusted Sword (1-3 then, 4-6 now).
    /// </summary>
    [Theory]
    [InlineData(200u, (ushort)1, 34.9, 1.0)]       // Cleave
    [InlineData(200u, (ushort)10, 45.7, 1.0)]
    [InlineData(201u, (ushort)1, 68.05, 1.0)]      // Ground Slam
    [InlineData(201u, (ushort)10, 89.65, 1.0)]
    [InlineData(202u, (ushort)1, 54.5, 1.0)]       // Hurled Axe
    [InlineData(202u, (ushort)10, 72.5, 2.0)]
    [InlineData(203u, (ushort)1, 22.35, 1.0)]      // Rend
    [InlineData(203u, (ushort)10, 29.55, 1.0)]
    [InlineData(204u, (ushort)1, 16.35, 1.0)]      // Shield Bash
    [InlineData(204u, (ushort)10, 21.75, 1.5)]
    [InlineData(220u, (ushort)1, 34.6, 1.0)]       // Quick Shot
    [InlineData(220u, (ushort)10, 43.9, 1.5)]
    [InlineData(221u, (ushort)1, 65.8, 1.0)]       // Piercing Arrow
    [InlineData(221u, (ushort)10, 84.4, 1.0)]
    [InlineData(222u, (ushort)1, 49.2, 1.0)]       // Scatter Shot
    [InlineData(222u, (ushort)10, 61.6, 3.0)]
    [InlineData(223u, (ushort)1, 37.3, 1.0)]       // Crippling Shot
    [InlineData(223u, (ushort)10, 48.1, 9.0)]
    [InlineData(224u, (ushort)1, 21.05, 1.0)]      // Binding Shot
    [InlineData(224u, (ushort)10, 27.25, 7.0)]
    [InlineData(210u, (ushort)1, 35.25, 1.0)]      // Spark Bolt
    [InlineData(210u, (ushort)10, 48.75, 1.0)]
    [InlineData(211u, (ushort)1, 55.2, 1.0)]       // Flame Burst
    [InlineData(211u, (ushort)10, 76.8, 1.0)]
    [InlineData(212u, (ushort)1, 68.5, 1.0)]       // Frost Fan
    [InlineData(212u, (ushort)10, 95.5, 1.0)]
    [InlineData(214u, (ushort)1, 15.3, 3.0)]       // Frost Bind
    [InlineData(214u, (ushort)10, 20.7, 3.0)]
    [InlineData(230u, (ushort)1, 29.4, 1.0)]       // Smite
    [InlineData(230u, (ushort)10, 40.2, 1.0)]
    [InlineData(231u, (ushort)1, 51.0, 1.0)]       // Radiant Pulse
    [InlineData(231u, (ushort)10, 69.0, 1.0)]
    [InlineData(232u, (ushort)1, 74.8, 1.0)]       // Mending Circle
    [InlineData(232u, (ushort)10, 96.4, 2.0)]
    [InlineData(213u, (ushort)1, 79.8, 1.0, true)]   // Ignite's Burn, the whole total
    [InlineData(213u, (ushort)10, 112.2, 1.0, true)]
    [InlineData(233u, (ushort)1, 47.2, 1.0, true)]   // Renew, the whole heal
    [InlineData(233u, (ushort)10, 61.6, 1.0, true)]
    [InlineData(203u, (ushort)1, 26.5, 1.0, true)]   // Rend's Bleed, the whole total
    [InlineData(203u, (ushort)10, 35.5, 2.5, true)]
    [InlineData(200u, (ushort)1, 39.6, 1.0, false, "forest")]    // Cleave, Bramblesteel Sword
    [InlineData(200u, (ushort)10, 50.4, 2.0, false, "forest")]   // tolerance: the closest whole-number range's measured gap (D2)
    [InlineData(220u, (ushort)1, 39.3, 4.0, false, "forest")]    // Quick Shot, Briarstring Bow; tolerance: the closest whole-number range's measured gap (D2)
    [InlineData(220u, (ushort)10, 48.9, 1.5, false, "forest")]
    [InlineData(200u, (ushort)1, 30.8, 1.5, false, "rusted")]    // Cleave, Rusted Sword; tolerance: the closest whole-number range's measured gap (D2)
    [InlineData(200u, (ushort)10, 41.6, 1.5, false, "rusted")]   // tolerance: the closest whole-number range's measured gap (D2)
    public void Carry_todays_ability_numbers_over_at_levels_one_and_ten(uint id, ushort level, double todayAvg,
        double tolerancePct, bool viaAura = false, string gear = "starter")
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        AbilityTemplate ability = context.AbilityTemplates.AsNoTracking().AsEnumerable().Single(a => a.Id.Value == id);
        CharacterClass c = Assert.Single(ability.AllowedClasses);
        CombatFormula formula = Assert.Single(context.CombatFormulas.AsNoTracking().ToList());
        ClassStatFactors factors = context.ClassStatFactors.AsNoTracking().AsEnumerable().Single(f => f.Class == c);
        ClassLevelStat row = context.ClassLevelStats.AsNoTracking().AsEnumerable().Single(s => s.Class == c && s.Level == level);
        List<WornItem> worn = RetiredGearProfiles.For(gear, c);

        AttackerCombat caster = CharacterStatsCalculator.Calculate(row, worn, factors, formula).AttackerAt(level);
        AbilityMetadata metadata = AbilityMetadataMapper.From(ability);
        AuraTemplate? aura = viaAura
            ? context.AuraTemplates.AsNoTracking().AsEnumerable().Single(a => a.Id.Value == ability.AuraId!.Value)
            : null;

        double At(ICombatRandom ends) =>
            aura is not null
                ? OffenceMath.PeriodicTotal(aura, caster, metadata, HitResolver.TypeOf(metadata.DamageType, caster), ends)
                : (metadata.Tags & AbilityTags.Heal) != 0
                    ? OffenceMath.HealBase(caster, metadata, ends)
                    : OffenceMath.DirectBase(caster, metadata, ends).Total;

        double average = (At(RollEnds.Low) + At(RollEnds.High)) / 2d;
        Assert.True(Math.Abs(average - todayAvg) / todayAvg * 100d <= tolerancePct,
            FormattableString.Invariant($"ability {id} at level {level}: {average:0.###} against today's {todayAvg}"));
    }

    private static StatType?[] StatTypesOf(ItemTemplate i) =>
    [
        i.StatType1, i.StatType2, i.StatType3, i.StatType4, i.StatType5,
        i.StatType6, i.StatType7, i.StatType8, i.StatType9, i.StatType10,
    ];

    /// <summary>The stat lines a template sets, in slot order.</summary>
    private static IEnumerable<(StatType?, uint?)> StatLinesOf(ItemTemplate i) =>
        new (StatType? Type, uint? Value)[]
        {
            (i.StatType1, i.StatValue1), (i.StatType2, i.StatValue2), (i.StatType3, i.StatValue3),
            (i.StatType4, i.StatValue4), (i.StatType5, i.StatValue5), (i.StatType6, i.StatValue6),
            (i.StatType7, i.StatValue7), (i.StatType8, i.StatValue8), (i.StatType9, i.StatValue9),
            (i.StatType10, i.StatValue10),
        }.Where(l => l.Type is not null).Select(l => (l.Type, l.Value));

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
        Assert.Equal((69ul, 1u), (Assert.Single(quests[2].ItemRewards).ItemTemplateId.Value, quests[2].ItemRewards[0].Count));
        Assert.Equal([32ul, 33ul, 34ul, 35ul], quests[2].ItemChoices.Select(c => c.ItemTemplateId.Value).Order());
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

        Assert.Equal([(4u, 2ul, 3u), (6u, 56ul, 2u), (7u, 68ul, 1u), (8u, 68ul, 1u)],
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
    /// #711: Hilde's "Earn It Fancy" after "Pelts Before Frost": the pack and its alpha, then one of three chest bases,
    /// one per armour family (#1179), and no fixed item. Pins the structure, not the seeded values.
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

        // One chest base per armour family to choose from.
        ulong[] choices = [.. quest.ItemChoices.Select(c => c.ItemTemplateId.Value).Order()];
        Assert.Equal([37ul, 42ul, 47ul], choices);
        Assert.All(quest.ItemChoices, c => Assert.Equal(1u, c.Count));
        Assert.Equal([ItemBaseKind.PlateChest, ItemBaseKind.LeatherChest, ItemBaseKind.ClothChest],
            context.ItemTemplates.AsNoTracking().ToList().Where(t => choices.Contains(t.Id.Value))
                .Select(t => t.BaseKind).Order());

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

    /// <summary>
    /// The chain's reward ring (#1179), the Copper Band base, for every class, in a finger slot, never flagged own-one-copy
    /// (a reward must always fit, and a player may wear two of one ring). The unique rings are pinned with the uniques.
    /// </summary>
    [Fact]
    public void Seed_the_chains_reward_ring_for_every_class()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        ItemTemplate ring = context.ItemTemplates.AsNoTracking().ToList().Single(i => i.Id.Value == 68);

        Assert.Equal("Copper Band", ring.Name);
        Assert.Equal((ItemClass.Armor, ItemSubClass.Ring, ItemSlotType.Finger, ItemRarity.Common, ItemBaseKind.Ring),
            (ring.Class, ring.SubClass, ring.Slot, ring.Rarity, ring.BaseKind));
        Assert.Equal((ushort)1, ring.RequiredLevel);
        Assert.Empty(ring.AllowedClasses);
        Assert.False(ring.Flags.HasFlag(ItemTemplateFlags.Unique));
        Assert.Equal(1u, ring.MaxStackSize);
    }

    /// <summary>
    /// #1179: every armour base as the spec's tables set it: its kind, tier level, the family's attribute from tier 2 and
    /// its price; a quarter of it back, every class, no stack, Common, and no stat line. Its defence is
    /// its one base stat, drafted from the family's (chest plate 8/18/32, leather 40/90/160, cloth 12/26/45; legs 80 %, head
    /// 60 %, shoulders 50 %, hands and feet 40 %), which its range spreads (<see cref="AssertDrafted" />).
    /// </summary>
    [Theory]
    [InlineData(36ul, ItemBaseKind.PlateHead, (ushort)1, ItemBaseStat.Armour, 5u, null, null, 60u)]
    [InlineData(37ul, ItemBaseKind.PlateChest, (ushort)1, ItemBaseStat.Armour, 8u, null, null, 100u)]
    [InlineData(38ul, ItemBaseKind.PlateLegs, (ushort)1, ItemBaseStat.Armour, 6u, null, null, 80u)]
    [InlineData(39ul, ItemBaseKind.PlateHands, (ushort)1, ItemBaseStat.Armour, 3u, null, null, 40u)]
    [InlineData(40ul, ItemBaseKind.PlateFeet, (ushort)1, ItemBaseStat.Armour, 3u, null, null, 40u)]
    [InlineData(76ul, ItemBaseKind.PlateShoulders, (ushort)1, ItemBaseStat.Armour, 4u, null, null, 50u)]
    [InlineData(12ul, ItemBaseKind.PlateHead, (ushort)5, ItemBaseStat.Armour, 11u, StatType.Strength, (ushort)30, 120u)]
    [InlineData(13ul, ItemBaseKind.PlateChest, (ushort)5, ItemBaseStat.Armour, 18u, StatType.Strength, (ushort)30, 200u)]
    [InlineData(14ul, ItemBaseKind.PlateLegs, (ushort)5, ItemBaseStat.Armour, 14u, StatType.Strength, (ushort)30, 160u)]
    [InlineData(15ul, ItemBaseKind.PlateHands, (ushort)5, ItemBaseStat.Armour, 7u, StatType.Strength, (ushort)30, 80u)]
    [InlineData(16ul, ItemBaseKind.PlateFeet, (ushort)5, ItemBaseStat.Armour, 7u, StatType.Strength, (ushort)30, 80u)]
    [InlineData(79ul, ItemBaseKind.PlateShoulders, (ushort)5, ItemBaseStat.Armour, 9u, StatType.Strength, (ushort)30, 100u)]
    [InlineData(85ul, ItemBaseKind.PlateHead, (ushort)10, ItemBaseStat.Armour, 19u, StatType.Strength, (ushort)45, 240u)]
    [InlineData(86ul, ItemBaseKind.PlateChest, (ushort)10, ItemBaseStat.Armour, 32u, StatType.Strength, (ushort)45, 400u)]
    [InlineData(87ul, ItemBaseKind.PlateLegs, (ushort)10, ItemBaseStat.Armour, 26u, StatType.Strength, (ushort)45, 320u)]
    [InlineData(88ul, ItemBaseKind.PlateHands, (ushort)10, ItemBaseStat.Armour, 13u, StatType.Strength, (ushort)45, 160u)]
    [InlineData(89ul, ItemBaseKind.PlateFeet, (ushort)10, ItemBaseStat.Armour, 13u, StatType.Strength, (ushort)45, 160u)]
    [InlineData(82ul, ItemBaseKind.PlateShoulders, (ushort)10, ItemBaseStat.Armour, 16u, StatType.Strength, (ushort)45, 200u)]
    [InlineData(46ul, ItemBaseKind.LeatherHead, (ushort)1, ItemBaseStat.Evasion, 24u, null, null, 60u)]
    [InlineData(47ul, ItemBaseKind.LeatherChest, (ushort)1, ItemBaseStat.Evasion, 40u, null, null, 100u)]
    [InlineData(48ul, ItemBaseKind.LeatherLegs, (ushort)1, ItemBaseStat.Evasion, 32u, null, null, 80u)]
    [InlineData(49ul, ItemBaseKind.LeatherHands, (ushort)1, ItemBaseStat.Evasion, 16u, null, null, 40u)]
    [InlineData(50ul, ItemBaseKind.LeatherFeet, (ushort)1, ItemBaseStat.Evasion, 16u, null, null, 40u)]
    [InlineData(77ul, ItemBaseKind.LeatherShoulders, (ushort)1, ItemBaseStat.Evasion, 20u, null, null, 50u)]
    [InlineData(22ul, ItemBaseKind.LeatherHead, (ushort)5, ItemBaseStat.Evasion, 54u, StatType.Agility, (ushort)30, 120u)]
    [InlineData(23ul, ItemBaseKind.LeatherChest, (ushort)5, ItemBaseStat.Evasion, 90u, StatType.Agility, (ushort)30, 200u)]
    [InlineData(24ul, ItemBaseKind.LeatherLegs, (ushort)5, ItemBaseStat.Evasion, 72u, StatType.Agility, (ushort)30, 160u)]
    [InlineData(25ul, ItemBaseKind.LeatherHands, (ushort)5, ItemBaseStat.Evasion, 36u, StatType.Agility, (ushort)30, 80u)]
    [InlineData(26ul, ItemBaseKind.LeatherFeet, (ushort)5, ItemBaseStat.Evasion, 36u, StatType.Agility, (ushort)30, 80u)]
    [InlineData(80ul, ItemBaseKind.LeatherShoulders, (ushort)5, ItemBaseStat.Evasion, 45u, StatType.Agility, (ushort)30, 100u)]
    [InlineData(90ul, ItemBaseKind.LeatherHead, (ushort)10, ItemBaseStat.Evasion, 96u, StatType.Agility, (ushort)45, 240u)]
    [InlineData(91ul, ItemBaseKind.LeatherChest, (ushort)10, ItemBaseStat.Evasion, 160u, StatType.Agility, (ushort)45, 400u)]
    [InlineData(92ul, ItemBaseKind.LeatherLegs, (ushort)10, ItemBaseStat.Evasion, 128u, StatType.Agility, (ushort)45, 320u)]
    [InlineData(93ul, ItemBaseKind.LeatherHands, (ushort)10, ItemBaseStat.Evasion, 64u, StatType.Agility, (ushort)45, 160u)]
    [InlineData(94ul, ItemBaseKind.LeatherFeet, (ushort)10, ItemBaseStat.Evasion, 64u, StatType.Agility, (ushort)45, 160u)]
    [InlineData(83ul, ItemBaseKind.LeatherShoulders, (ushort)10, ItemBaseStat.Evasion, 80u, StatType.Agility, (ushort)45, 200u)]
    [InlineData(41ul, ItemBaseKind.ClothHead, (ushort)1, ItemBaseStat.Ward, 7u, null, null, 60u)]
    [InlineData(42ul, ItemBaseKind.ClothChest, (ushort)1, ItemBaseStat.Ward, 12u, null, null, 100u)]
    [InlineData(43ul, ItemBaseKind.ClothLegs, (ushort)1, ItemBaseStat.Ward, 10u, null, null, 80u)]
    [InlineData(44ul, ItemBaseKind.ClothHands, (ushort)1, ItemBaseStat.Ward, 5u, null, null, 40u)]
    [InlineData(45ul, ItemBaseKind.ClothFeet, (ushort)1, ItemBaseStat.Ward, 5u, null, null, 40u)]
    [InlineData(78ul, ItemBaseKind.ClothShoulders, (ushort)1, ItemBaseStat.Ward, 6u, null, null, 50u)]
    [InlineData(17ul, ItemBaseKind.ClothHead, (ushort)5, ItemBaseStat.Ward, 16u, StatType.Intellect, (ushort)30, 120u)]
    [InlineData(18ul, ItemBaseKind.ClothChest, (ushort)5, ItemBaseStat.Ward, 26u, StatType.Intellect, (ushort)30, 200u)]
    [InlineData(19ul, ItemBaseKind.ClothLegs, (ushort)5, ItemBaseStat.Ward, 21u, StatType.Intellect, (ushort)30, 160u)]
    [InlineData(20ul, ItemBaseKind.ClothHands, (ushort)5, ItemBaseStat.Ward, 10u, StatType.Intellect, (ushort)30, 80u)]
    [InlineData(21ul, ItemBaseKind.ClothFeet, (ushort)5, ItemBaseStat.Ward, 10u, StatType.Intellect, (ushort)30, 80u)]
    [InlineData(81ul, ItemBaseKind.ClothShoulders, (ushort)5, ItemBaseStat.Ward, 13u, StatType.Intellect, (ushort)30, 100u)]
    [InlineData(95ul, ItemBaseKind.ClothHead, (ushort)10, ItemBaseStat.Ward, 27u, StatType.Intellect, (ushort)45, 240u)]
    [InlineData(96ul, ItemBaseKind.ClothChest, (ushort)10, ItemBaseStat.Ward, 45u, StatType.Intellect, (ushort)45, 400u)]
    [InlineData(97ul, ItemBaseKind.ClothLegs, (ushort)10, ItemBaseStat.Ward, 36u, StatType.Intellect, (ushort)45, 320u)]
    [InlineData(98ul, ItemBaseKind.ClothHands, (ushort)10, ItemBaseStat.Ward, 18u, StatType.Intellect, (ushort)45, 160u)]
    [InlineData(99ul, ItemBaseKind.ClothFeet, (ushort)10, ItemBaseStat.Ward, 18u, StatType.Intellect, (ushort)45, 160u)]
    [InlineData(84ul, ItemBaseKind.ClothShoulders, (ushort)10, ItemBaseStat.Ward, 23u, StatType.Intellect, (ushort)45, 200u)]
    public void Seed_every_armour_base_as_the_spec_tables_set_it(ulong id, ItemBaseKind kind, ushort level, ItemBaseStat stat,
        uint value, StatType? requirement, ushort? amount, uint buy)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        ItemTemplate piece = context.ItemTemplates.AsNoTracking().ToList().Single(i => i.Id.Value == id);

        Assert.Equal((kind, (ushort?)level, ItemClass.Armor, (uint)id), (piece.BaseKind, piece.RequiredLevel, piece.Class, piece.DisplayId));
        Assert.Empty(StatLinesOf(piece));
        AssertDrafted(id, (stat, (int)value));
        Assert.Equal(
            (requirement == StatType.Strength ? amount : null, requirement == StatType.Agility ? amount : null,
                requirement == StatType.Intellect ? amount : null),
            (piece.RequiredStrength, piece.RequiredAgility, piece.RequiredIntellect));
        Assert.Equal((buy, buy / 4), (piece.BuyPrice, piece.SellPrice));
        Assert.Empty(piece.AllowedClasses);
        Assert.Equal((1u, ItemRarity.Common, false), (piece.MaxStackSize, piece.Rarity, piece.RequiresAnyAttribute));
    }

    /// <summary>
    /// #1179: every weapon and off-hand base. <paramref name="min" />, <paramref name="max" /> and
    /// <paramref name="attackTime" /> are its drafted base stats (<see cref="AssertDrafted" />): a weapon's damage ends
    /// and attack time, its template naming physical damage; a shield's armour and block, a caster off-hand's ward alone
    /// (its <paramref name="max" /> 0), with no damage type. None carries a stat line. Each asks its attribute (a sword Strength, a mace Strength or Intellect, a staff
    /// Intellect, a bow Agility, a shield Strength, a caster off-hand Intellect) from tier 2, 30 then 45, and costs 120,
    /// 240 or 480 by tier.
    /// </summary>
    [Theory]
    [InlineData(32ul, ItemBaseKind.Sword, (ushort)1, 3u, 7u, 700, false)]
    [InlineData(7ul, ItemBaseKind.Sword, (ushort)5, 6u, 10u, 700, false)]
    [InlineData(100ul, ItemBaseKind.Sword, (ushort)10, 9u, 16u, 700, false)]
    [InlineData(35ul, ItemBaseKind.Mace, (ushort)1, 4u, 9u, 900, true)]
    [InlineData(8ul, ItemBaseKind.Mace, (ushort)5, 7u, 14u, 900, true)]
    [InlineData(101ul, ItemBaseKind.Mace, (ushort)10, 12u, 21u, 900, true)]
    [InlineData(33ul, ItemBaseKind.Staff, (ushort)1, 5u, 9u, 1000, false)]
    [InlineData(5ul, ItemBaseKind.Staff, (ushort)5, 8u, 15u, 1000, false)]
    [InlineData(102ul, ItemBaseKind.Staff, (ushort)10, 13u, 22u, 1000, false)]
    [InlineData(34ul, ItemBaseKind.Bow, (ushort)1, 4u, 7u, 800, false)]
    [InlineData(6ul, ItemBaseKind.Bow, (ushort)5, 7u, 11u, 800, false)]
    [InlineData(103ul, ItemBaseKind.Bow, (ushort)10, 11u, 17u, 800, false)]
    [InlineData(70ul, ItemBaseKind.Shield, (ushort)1, 6u, 5u, null, false)]
    [InlineData(71ul, ItemBaseKind.Shield, (ushort)5, 14u, 7u, null, false)]
    [InlineData(72ul, ItemBaseKind.Shield, (ushort)10, 24u, 10u, null, false)]
    [InlineData(73ul, ItemBaseKind.CasterOffHand, (ushort)1, 8u, 0u, null, false)]
    [InlineData(74ul, ItemBaseKind.CasterOffHand, (ushort)5, 18u, 0u, null, false)]
    [InlineData(75ul, ItemBaseKind.CasterOffHand, (ushort)10, 30u, 0u, null, false)]
    public void Seed_every_weapon_and_off_hand_base(ulong id, ItemBaseKind kind, ushort level, uint min, uint max,
        int? attackTime, bool any)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        ItemTemplate item = context.ItemTemplates.AsNoTracking().ToList().Single(i => i.Id.Value == id);

        Assert.Equal((kind, (ushort?)level, any), (item.BaseKind, item.RequiredLevel, item.RequiresAnyAttribute));
        if (kind is ItemBaseKind.Shield or ItemBaseKind.CasterOffHand)
        {
            ItemSubClass subClass = kind == ItemBaseKind.Shield ? ItemSubClass.Shield : ItemSubClass.CasterOffHand;
            Assert.Equal((ItemClass.Armor, subClass, (ItemSlotType?)ItemSlotType.OffHand), (item.Class, item.SubClass, item.Slot));
            if (kind == ItemBaseKind.Shield)
                AssertDrafted(id, (ItemBaseStat.Armour, (int)min), (ItemBaseStat.BlockChance, (int)max));
            else
                AssertDrafted(id, (ItemBaseStat.Ward, (int)min));
            Assert.Null(item.DamageType);
        }
        else
        {
            Assert.Equal((ItemClass.Weapon, (ItemSlotType?)ItemSlotType.MainHand), (item.Class, item.Slot));
            AssertDrafted(id, (ItemBaseStat.DamageMin, (int)min), (ItemBaseStat.DamageMax, (int)max),
                (ItemBaseStat.AttackTime, attackTime!.Value));
            Assert.Equal(DamageType.Physical, item.DamageType);
        }

        Assert.Empty(StatLinesOf(item));

        ushort? amount = level switch { 1 => null, 5 => (ushort)30, _ => (ushort)45 };
        (bool strength, bool agility, bool intellect) = kind switch
        {
            ItemBaseKind.Sword or ItemBaseKind.Shield => (true, false, false),
            ItemBaseKind.Mace => (true, false, true),
            ItemBaseKind.Bow => (false, true, false),
            _ => (false, false, true),
        };
        Assert.Equal((strength ? amount : null, agility ? amount : null, intellect ? amount : null),
            (item.RequiredStrength, item.RequiredAgility, item.RequiredIntellect));
        uint buy = level switch { 1 => 120u, 5 => 240u, _ => 480u };
        Assert.Equal((buy, buy / 4), (item.BuyPrice, item.SellPrice));
        Assert.Empty(item.AllowedClasses);
        Assert.Equal((1u, ItemRarity.Common), (item.MaxStackSize, item.Rarity));
    }

    /// <summary>
    /// #1179: a sample of every base's authored base-stat ranges, the content table's min-max columns: each drafted value
    /// ±20 % (an attack time ±5 %), each end rounded half away from zero and at least 1.
    /// </summary>
    [Theory]
    [InlineData(37ul, ItemBaseStat.Armour, 6, 10)]
    [InlineData(86ul, ItemBaseStat.Armour, 26, 38)]
    [InlineData(39ul, ItemBaseStat.Armour, 2, 4)]
    [InlineData(91ul, ItemBaseStat.Evasion, 128, 192)]
    [InlineData(96ul, ItemBaseStat.Ward, 36, 54)]
    [InlineData(70ul, ItemBaseStat.Armour, 5, 7)]
    [InlineData(70ul, ItemBaseStat.BlockChance, 4, 6)]
    [InlineData(72ul, ItemBaseStat.BlockChance, 8, 12)]
    [InlineData(75ul, ItemBaseStat.Ward, 24, 36)]
    [InlineData(32ul, ItemBaseStat.DamageMin, 2, 4)]
    [InlineData(32ul, ItemBaseStat.DamageMax, 6, 8)]
    [InlineData(32ul, ItemBaseStat.AttackTime, 665, 735)]
    [InlineData(101ul, ItemBaseStat.DamageMin, 10, 14)]
    [InlineData(101ul, ItemBaseStat.DamageMax, 17, 25)]
    [InlineData(101ul, ItemBaseStat.AttackTime, 855, 945)]
    [InlineData(102ul, ItemBaseStat.DamageMax, 18, 26)]
    [InlineData(6ul, ItemBaseStat.DamageMin, 6, 8)]
    [InlineData(34ul, ItemBaseStat.AttackTime, 760, 840)]
    [InlineData(33ul, ItemBaseStat.AttackTime, 950, 1050)]
    public void Seed_each_bases_base_stat_ranges(ulong template, ItemBaseStat stat, int min, int max)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        ItemTemplateBaseStat row = context.ItemTemplateBaseStats.AsNoTracking().ToList()
            .Single(s => s.TemplateId.Value == template && s.Stat == stat);

        Assert.Equal((min, max), (row.Min, row.Max));
    }

    /// <summary>
    /// #1179: every seeded template carries exactly the base stats its kind asks, each range sound and a damage type
    /// exactly on a weapon (ItemBaseStatRules.Problem), so the world leaves none out; jewellery, trinkets and uniques
    /// carry none. Within a kind's line, each tier's range starts at or above the tier below's end, so a better tier never
    /// rolls worse; an attack time is the kind's pace and the same at every tier.
    /// </summary>
    [Fact]
    public void Keep_every_bases_base_stats_to_its_kind()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var templates = context.ItemTemplates.AsNoTracking().ToList();

        Assert.All(templates, t => Assert.True(ItemBaseStatRules.Problem(t) is null, $"{t.Name}: {ItemBaseStatRules.Problem(t)}"));
        Assert.All(
            templates.Where(t => t.Rarity == ItemRarity.Unique
                                 || t.BaseKind is ItemBaseKind.Ring or ItemBaseKind.Amulet or ItemBaseKind.Trinket),
            t => Assert.Empty(t.BaseStats));

        foreach (IGrouping<ItemBaseKind, ItemTemplate> line in templates
                     .Where(t => t.Rarity == ItemRarity.Common && t.BaseKind is not null)
                     .GroupBy(t => t.BaseKind!.Value))
        {
            ItemTemplate[] tiers = [.. line.OrderBy(t => t.RequiredLevel)];
            for (int tier = 1; tier < tiers.Length; tier++)
            {
                foreach (ItemTemplateBaseStat range in tiers[tier].BaseStats)
                {
                    ItemTemplateBaseStat below = tiers[tier - 1].BaseStats.Single(s => s.Stat == range.Stat);
                    if (range.Stat == ItemBaseStat.AttackTime)
                        Assert.Equal((below.Min, below.Max), (range.Min, range.Max));
                    else
                        Assert.True(range.Min >= below.Max, $"{tiers[tier].Name}'s {range.Stat} starts under {tiers[tier - 1].Name}'s end");
                }
            }
        }
    }

    /// <summary>
    /// #1179: the five uniques as templates without stats: name, kind, slot, required level and prices only, Unique. A
    /// unique's stats are its unique modifier, rolled per copy when it drops, so no vendor row, quest reward, quest choice
    /// or starting item names one: those would hand out a copy made outright. Only loot names them (pinned below).
    /// </summary>
    [Fact]
    public void Seed_the_five_uniques_as_templates_without_stats()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var items = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(i => i.Id.Value);

        (ulong Id, string Name, ItemBaseKind Kind, ItemSubClass SubClass, ItemSlotType Slot, ushort Level, uint Sell)[] uniques =
        [
            (58, "Alpha's Fang Pendant", ItemBaseKind.Amulet, ItemSubClass.Amulet, ItemSlotType.Neck, 3, 50),
            (64, "Heartwood Band", ItemBaseKind.Ring, ItemSubClass.Ring, ItemSlotType.Finger, 4, 100),
            (65, "Thornheart Signet", ItemBaseKind.Ring, ItemSubClass.Ring, ItemSlotType.Finger, 5, 200),
            (66, "Bramble Mother's Seed", ItemBaseKind.Trinket, ItemSubClass.Trinket, ItemSlotType.Trinket, 5, 200),
            (67, "Tuskroot Idol", ItemBaseKind.Trinket, ItemSubClass.Trinket, ItemSlotType.Trinket, 4, 100),
        ];

        Assert.Equal(uniques.Select(u => u.Id), items.Values.Where(i => i.Rarity == ItemRarity.Unique).Select(i => i.Id.Value).Order());
        foreach ((ulong id, string name, ItemBaseKind kind, ItemSubClass subClass, ItemSlotType slot, ushort level, uint sell) in uniques)
        {
            ItemTemplate unique = items[id];
            Assert.Equal((name, ItemClass.Armor, subClass, (ItemSlotType?)slot, (ItemBaseKind?)kind),
                (unique.Name, unique.Class, unique.SubClass, unique.Slot, unique.BaseKind));
            Assert.Equal(((ushort?)level, (ushort?)null, (ushort?)null, (ushort?)null),
                (unique.RequiredLevel, unique.RequiredStrength, unique.RequiredAgility, unique.RequiredIntellect));
            Assert.Equal((sell, 4 * sell, ItemTemplateFlags.None, 1u), (unique.SellPrice, unique.BuyPrice, unique.Flags, unique.MaxStackSize));
            Assert.Empty(unique.AllowedClasses);
            Assert.All(StatTypesOf(unique), Assert.Null);
        }

        HashSet<ulong> ids = [.. uniques.Select(u => u.Id)];
        Assert.DoesNotContain(context.VendorStocks.AsNoTracking().ToList(), v => ids.Contains(v.ItemTemplateId.Value));
        Assert.DoesNotContain(context.QuestItemRewards.AsNoTracking().ToList(), r => ids.Contains(r.ItemTemplateId.Value));
        Assert.DoesNotContain(context.QuestItemChoices.AsNoTracking().ToList(), c => ids.Contains(c.ItemTemplateId.Value));
        Assert.DoesNotContain(context.CharacterCreateInfos.AsNoTracking().ToList().SelectMany(c => c.StartingItems),
            i => ids.Contains(i.Value));
    }

    /// <summary>
    /// #1179: the jewellery table as drafted; every loot table, vendor row, quest reward, quest choice and starting item
    /// names a template that exists; every worn template is a base kind, worn in its kind's slot, that never stacks, and
    /// nothing else has a base kind. The weapon and armour tables, the vendors' lines and the quests' rewards are pinned
    /// by their own tests above and in VendorSeedShould.
    /// </summary>
    [Fact]
    public void Seed_vendors_loot_and_quest_rewards_on_the_bases()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var items = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(i => i.Id.Value);
        var entries = context.LootTableEntries.AsNoTracking().ToList();

        // The uniques drop from the elite and boss tables alone, each its own entry: Alpha's Fang Pendant from the
        // Bramblemaw Alpha (6), the Heartwood Band and the Tuskroot Idol from Old Tuskroot (7), the Thornheart Signet and
        // Bramble Mother's Seed from Mother Bramble (8).
        Assert.Equal(
            [(6, 58ul, 10f, (int?)null), (7, 64ul, 10f, null), (7, 67ul, 10f, null), (8, 65ul, 15f, null), (8, 66ul, 15f, null)],
            entries.Where(e => e.ItemTemplateId is { } item && items[item.Value].Rarity == ItemRarity.Unique)
                .OrderBy(e => e.LootTableId.Value).ThenBy(e => e.ItemTemplateId!.Value)
                .Select(e => (e.LootTableId.Value, e.ItemTemplateId!.Value, e.Chance, e.GroupId)));

        LootTable jewellery = LoadTable(context, s_forestJewellery);
        Assert.Equal("Forest jewellery", jewellery.Name);
        Assert.Equal([(1, 68ul, 2f, (int?)null), (2, 69ul, 2f, null)],
            jewellery.Entries.OrderBy(e => e.Sequence).Select(e => (e.Sequence, e.ItemTemplateId!.Value, e.Chance, e.GroupId)));
        Assert.Single(entries, e => e.LootTableId.Value == 12 && e.ReferenceTableId == s_forestJewellery && e.Chance == 100f);

        IEnumerable<ulong> named = entries.Where(e => e.ItemTemplateId is not null).Select(e => e.ItemTemplateId!.Value)
            .Concat(context.VendorStocks.AsNoTracking().ToList().Select(v => v.ItemTemplateId.Value))
            .Concat(context.QuestItemRewards.AsNoTracking().ToList().Select(r => r.ItemTemplateId.Value))
            .Concat(context.QuestItemChoices.AsNoTracking().ToList().Select(c => c.ItemTemplateId.Value))
            .Concat(context.CharacterCreateInfos.AsNoTracking().ToList().SelectMany(c => c.StartingItems).Select(i => i.Value));
        Assert.All(named, id => Assert.Contains(id, items.Keys));
        Assert.All(items.Values.Where(i => i.Slot is not null), i => Assert.Equal(1u, i.MaxStackSize));
        Assert.All(items.Values, i => Assert.True((i.Slot is null) == (i.BaseKind is null),
            $"{i.Name}: a worn template is a base kind, and nothing else is"));
        Assert.All(items.Values.Where(i => i.BaseKind is not null),
            i => Assert.Equal(ItemBaseKindSlots.SlotOf(i.BaseKind!.Value), i.Slot));
    }

    /// <summary>
    /// #1179: Mother Bramble's table rolls the forest boss gear on every kill, one group of a first-tier base of every
    /// kind but the trinket (whose bases only uniques name) at equal weight, so a boss kill always has a drop its
    /// guaranteed rare can be. A kind with several lines (rings, amulets) names its first line's, the Copper Band and the
    /// Bone Charm, and the drop draws its line.
    /// </summary>
    [Fact]
    public void Seed_the_forest_boss_gear_as_one_group_of_a_first_tier_base_per_kind()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var items = context.ItemTemplates.AsNoTracking().ToList();
        LootTable gear = LoadTable(context, s_forestBossGear);

        Assert.Equal("Forest boss gear", gear.Name);
        Assert.Equal(26, gear.Entries.Count);
        Assert.All(gear.Entries, e => Assert.Equal(((int?)1, gear.Entries.First().Chance, 1, 1), (e.GroupId, e.Chance, e.MinCount, e.MaxCount)));
        Assert.True(gear.Entries.First().Chance > 0f);
        IEnumerable<ulong> tierOnes = items
            .Where(i => i.Rarity == ItemRarity.Common && i.BaseKind is not null and not ItemBaseKind.Trinket)
            .GroupBy(i => i.BaseKind)
            .Select(kind => kind.OrderBy(i => i.RequiredLevel ?? 1).ThenBy(i => i.Id.Value).First().Id.Value);
        Assert.Equal(tierOnes.Order(), gear.Entries.Select(e => e.ItemTemplateId!.Value).Order());
        Assert.Single(context.LootTableEntries.AsNoTracking().ToList(), e => e.ReferenceTableId == s_forestBossGear);
        Assert.Single(LoadTable(context, new LootTableId(8)).Entries,
            e => e.ReferenceTableId == s_forestBossGear && e.Chance == 100f && e.GroupId is null);
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

    /// <summary>
    /// #1179: the modifier catalog whole, as the database holds it. Its counts (its ids are held by
    /// <see cref="Keep_every_catalog_row_at_its_committed_id" />): 45 affix groups and 26 implicit groups of three tiers
    /// each, T9, T8 and T7 (Part B's fourth and fifth tiers are gone, and no row has a tier from 1 to 6), the affixes
    /// weighing on the 222 pairs of a group and a base kind it rolls on; the rules that span its tables (a code on every
    /// affix group, an element only on a split family's, one stat set per group, a tier on every affix and implicit row and
    /// on no unique's); the tiers' item levels and weights; the second range on exactly the "adds" lines; no weight on an
    /// implicit, a unique or a trinket; and the template links: each unique links its own unique row, each of the 122 bases
    /// one implicit row (<see cref="Seed_every_base_with_one_tier_matched_implicit" />), and nothing else links anything
    /// (Part B's per-base implicits, groups 50-109 and rows 224-283, are gone, their ids unused). The catalog loads all of
    /// it without leaving any out.
    /// </summary>
    [Fact]
    public async Task Seed_the_catalog_whole()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var groups = context.ItemModifierGroups.AsNoTracking().OrderBy(g => g.Id).ToList();
        var modifiers = context.ItemModifiers.AsNoTracking().AsSplitQuery()
            .Include(m => m.Stats).Include(m => m.Weights).OrderBy(m => m.Id).ToList();
        var links = context.ItemTemplateModifiers.AsNoTracking().ToList();
        var templates = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(t => t.Id.Value);
        var groupOf = modifiers.ToDictionary(m => m.Id, m => groups.Single(g => g.Id == m.GroupId));

        int Count(ItemModifierKind kind) => groups.Count(g => g.Kind == kind);
        Assert.Equal((76, 28, 17, 5, 26), (groups.Count, Count(ItemModifierKind.Prefix), Count(ItemModifierKind.Suffix),
            Count(ItemModifierKind.Unique), Count(ItemModifierKind.Implicit)));
        Assert.Equal((218, 228, 666, 127),
            (modifiers.Count, modifiers.Sum(m => m.Stats.Count), modifiers.Sum(m => m.Weights.Count), links.Count));

        var templateOfUnique = links.Where(l => groupOf[l.ModifierId].Kind == ItemModifierKind.Unique)
            .ToDictionary(l => groupOf[l.ModifierId].Id, l => l.TemplateId.Value);

        string[] split = ["P8", "P9", "P10", "S5"];
        Dictionary<byte, (ushort Level, int Weight)> tiers = new() { [9] = (1, 1000), [8] = (4, 800), [7] = (7, 600) };
        foreach (ItemModifierGroup group in groups)
        {
            List<ItemModifier> rows = [.. modifiers.Where(m => m.GroupId == group.Id)];
            Assert.Single(rows.Select(m => string.Join(" ", m.Stats.OrderBy(s => s.Index).Select(s => s.Stat))).Distinct());
            if (group.Kind is ItemModifierKind.Unique)
            {
                Assert.Equal((null, null), (group.Code, group.Element));
                ItemModifier row = Assert.Single(rows);
                Assert.Equal(((byte?)null, (ushort)1), (row.Tier, row.RequiredItemLevel));
                Assert.Empty(row.Weights);
                Assert.Equal(templates[templateOfUnique[group.Id]].Name, group.Name);
                continue;
            }

            bool affix = group.Kind is ItemModifierKind.Prefix or ItemModifierKind.Suffix;
            Assert.Equal(affix, group.Code is not null);
            Assert.Equal(affix && split.Contains(group.Code), group.Element is not null);
            Assert.Equal(new byte?[] { 7, 8, 9 }, rows.Select(m => m.Tier).Order());
            foreach (ItemModifier row in rows)
            {
                Assert.Equal(tiers[row.Tier!.Value].Level, row.RequiredItemLevel);
                Assert.Equal(affix, row.Weights.Count > 0);
                Assert.All(row.Weights, w => Assert.Equal(tiers[row.Tier!.Value].Weight, w.Weight));
            }

            // Every tier of a group rolls on the same base kinds.
            Assert.Single(rows.Select(m => string.Join(" ", m.Weights.Select(w => w.BaseKind).Order())).Distinct());
        }

        static bool AddsDamage(StatType stat) =>
            stat is >= StatType.AddedPhysicalDamage and <= StatType.AddedBlightDamage
                or >= StatType.AddedPhysicalDamageToAttacks and <= StatType.AddedLightningDamageToSpells;
        Assert.All(modifiers.SelectMany(m => m.Stats), s =>
            Assert.Equal((AddsDamage(s.Stat), AddsDamage(s.Stat)), (s.Min2 is not null, s.Max2 is not null)));
        Assert.DoesNotContain(modifiers.SelectMany(m => m.Weights), w => w.BaseKind == ItemBaseKind.Trinket);

        // A unique links its own unique row and nothing else, a base one implicit row, and no other template anything.
        Assert.All(links, l => Assert.Equal(0, l.Index));
        Assert.Equal(
            templates.Values.Where(t => t.Rarity == ItemRarity.Unique).Select(t => t.Id.Value).Order(),
            links.Where(l => groupOf[l.ModifierId].Kind == ItemModifierKind.Unique).Select(l => l.TemplateId.Value).Order());
        Assert.Equal(
            templates.Values.Where(t => t.Rarity == ItemRarity.Common && t.BaseKind is not null).Select(t => t.Id.Value).Order(),
            links.Where(l => groupOf[l.ModifierId].Kind == ItemModifierKind.Implicit).Select(l => l.TemplateId.Value).Order());
        Assert.DoesNotContain(modifiers, m => m.Id is >= 224 and <= 283);
        Assert.DoesNotContain(groups, g => g.Id is >= 50 and <= 109);

        ItemModifierCatalog catalog = ItemsPatch.Build(templates.Values, await new ItemModifierRepository(database,
            NullLoggerFactory.Instance).GetCatalogAsync(CancellationToken.None), NullLogger.Instance).Modifiers;
        Assert.Equal(218, catalog.Count);
        Assert.All(links, l => Assert.Equal(l.ModifierId, Assert.Single(catalog.LinkedTo(l.TemplateId)).Id));
    }

    [Theory]
    [InlineData("P1 T9", StatType.AddedPhysicalDamage, 1, 1, 1, 1, (ushort)1)]
    [InlineData("P1 T7", StatType.AddedPhysicalDamage, 2, 2, 3, 3, (ushort)7)]
    [InlineData("P4 T8", StatType.AddedLightningDamage, 1, 1, 3, 3, (ushort)4)]
    [InlineData("P6 T7", StatType.LocalIncreasedPhysicalDamage, 13, 16, null, null, (ushort)7)]
    [InlineData("P8 Cold T8", StatType.IncreasedColdDamage, 2, 3, null, null, (ushort)4)]
    [InlineData("P9 Physical T7", StatType.AddedPhysicalDamageToAttacks, 1, 1, 3, 3, (ushort)7)]
    [InlineData("P11 T7", StatType.Health, 5, 5, null, null, (ushort)7)]
    [InlineData("P14 T8", StatType.Evasion, 9, 14, null, null, (ushort)4)]
    [InlineData("S5 Blight T8", StatType.BlightResistance, 2, 3, null, null, (ushort)4)]
    [InlineData("S7 T7", StatType.AttackSpeed, 3, 3, null, null, (ushort)7)]
    [InlineData("S11 T9", StatType.MovementSpeed, 2, 2, null, null, (ushort)1)]
    [InlineData("S13 T9", StatType.BlockPct, 1, 2, null, null, (ushort)1)]
    [InlineData("S14 T7", StatType.IncreasedMaximumLife, 3, 4, null, null, (ushort)7)]
    public void Seed_catalog_rows_as_the_catalog_lists_them(string key, StatType stat, int min, int max, int? min2, int? max2,
        ushort requiredItemLevel)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        int id = CatalogKeys.ModifierId(key);
        ItemModifier row = context.ItemModifiers.AsNoTracking().Include(m => m.Stats).Single(m => m.Id == id);

        ItemModifierStat line = Assert.Single(row.Stats);
        Assert.Equal((stat, min, max, min2, max2, requiredItemLevel),
            (line.Stat, line.Min, line.Max, line.Min2, line.Max2, row.RequiredItemLevel));
    }

    /// <summary>
    /// #1179: where each family rolls, one row per base kind (the content table's "Families per slot"): the catalog codes
    /// with a group weighing above 0 on the kind. A split code is written bare when every one of its elements rolls there,
    /// and with its element ("S5 Fire") when only that one does.
    /// </summary>
    [Theory]
    [InlineData(ItemBaseKind.PlateHead, "P11 P12 P13", "S5 Fire S10 S12")]
    [InlineData(ItemBaseKind.LeatherShoulders, "P8 P14 P15", "S3 S12 S14")]
    [InlineData(ItemBaseKind.ClothChest, "P11 P16 P17", "S1 S5 Blight S14")]
    [InlineData(ItemBaseKind.PlateHands, "P9 P12 P13", "S7 S8 S9 S10")]
    [InlineData(ItemBaseKind.LeatherLegs, "P11 P14 P15", "S2 S5 Lightning S14")]
    [InlineData(ItemBaseKind.ClothFeet, "P11 P16 P17", "S4 S5 Cold S11")]
    [InlineData(ItemBaseKind.Amulet, "P8 P11 P18 P19", "S5")]
    [InlineData(ItemBaseKind.Ring, "P8 P10", "S1 S2 S3")]
    [InlineData(ItemBaseKind.Sword, "P1 P2 P3 P4 P5 P6", "S6 S9 S10")]
    [InlineData(ItemBaseKind.Mace, "P1 P2 P3 P4 P5 P6 P7 P19", "S6 S9 S10")]
    [InlineData(ItemBaseKind.Staff, "P1 P2 P3 P4 P5 P6 P7 P8 P18 P19", "S6 S8 S9 S10")]
    [InlineData(ItemBaseKind.Shield, "P11 P12 P13", "S5 S13")]
    [InlineData(ItemBaseKind.CasterOffHand, "P7 P8 P16 P17 P18 P19", "S5 S8 S10")]
    [InlineData(ItemBaseKind.Trinket, "", "")]
    public void Let_each_base_kind_roll_exactly_its_families(ItemBaseKind kind, string prefixes, string suffixes)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var groups = context.ItemModifierGroups.AsNoTracking().ToList();
        HashSet<int> rolling =
        [
            .. context.ItemModifiers.AsNoTracking().Include(m => m.Weights).ToList()
                .Where(m => m.Weights.Any(w => w.BaseKind == kind && w.Weight > 0)).Select(m => m.GroupId),
        ];

        string Codes(ItemModifierKind affix)
        {
            List<string> codes = [];
            foreach (IGrouping<string, ItemModifierGroup> code in groups.Where(g => g.Kind == affix)
                         .GroupBy(g => g.Code!).OrderBy(c => int.Parse(c.Key[1..])))
            {
                ItemModifierGroup[] rolls = [.. code.Where(g => rolling.Contains(g.Id))];
                if (rolls.Length == code.Count())
                    codes.Add(code.Key);
                else
                    codes.AddRange(rolls.Select(g => $"{code.Key} {g.Element}"));
            }

            return string.Join(" ", codes);
        }

        Assert.Equal((prefixes, suffixes), (Codes(ItemModifierKind.Prefix), Codes(ItemModifierKind.Suffix)));
    }

    /// <summary>
    /// #1179: the tiers progress. In every affix group each tier's best value (its maximum, or an "adds" line's high end)
    /// is above the one below it: T8 beats T9 and T7 beats T8, so a better tier never reads as a worse one.
    /// </summary>
    [Fact]
    public void Beat_the_tier_below_at_its_best_value()
    {
        Dictionary<int, ItemModifierGroup> groups = ItemModifierSeed.Groups().ToDictionary(g => g.Id);
        ILookup<int, ItemModifierStat> lines = ItemModifierSeed.Stats().ToLookup(s => s.ModifierId);

        foreach (IGrouping<int, ItemModifier> rows in ItemModifierSeed.Modifiers()
                     .Where(m => groups[m.GroupId].Kind is ItemModifierKind.Prefix or ItemModifierKind.Suffix)
                     .GroupBy(m => m.GroupId))
        {
            int[] best = [.. rows.OrderByDescending(m => m.Tier).Select(m => lines[m.Id].Single()).Select(line => line.Max2 ?? line.Max)];
            ItemModifierGroup group = groups[rows.Key];
            Assert.True(best.Zip(best.Skip(1)).All(pair => pair.First < pair.Second),
                $"{group.Code} {group.Element}: best values {string.Join(" / ", best)} from T9 up");
        }
    }

    /// <summary>
    /// #1179: each unique's own lines, the content table's Uniques column as authored for the starting zone's budget; the
    /// lines it dropped are held off by <see cref="Fit_the_starting_zone_budget" />. The Thornheart Signet's "adds 1 to 3"
    /// names its ends exactly: its low end 1-1 and its high end 3-3.
    /// </summary>
    [Theory]
    [InlineData(58ul, StatType.Health, 4, 5, null, null)]
    [InlineData(58ul, StatType.Strength, 2, 3, null, null)]
    [InlineData(58ul, StatType.Agility, 2, 3, null, null)]
    [InlineData(58ul, StatType.IncreasedAttackDamage, 6, 10, null, null)]
    [InlineData(64ul, StatType.Health, 4, 5, null, null)]
    [InlineData(64ul, StatType.BlightResistance, 5, 6, null, null)]
    [InlineData(64ul, StatType.IncreasedHealing, 5, 7, null, null)]
    [InlineData(65ul, StatType.Health, 4, 5, null, null)]
    [InlineData(65ul, StatType.AddedPhysicalDamageToAttacks, 1, 1, 3, 3)]
    [InlineData(65ul, StatType.CritMultiplier, 3, 4, null, null)]
    [InlineData(66ul, StatType.IncreasedMaximumLife, 4, 5, null, null)]
    [InlineData(66ul, StatType.IncreasedDamageOverTime, 2, 3, null, null)]
    [InlineData(67ul, StatType.IncreasedMaximumLife, 4, 5, null, null)]
    [InlineData(67ul, StatType.IncreasedAttackDamage, 5, 7, null, null)]
    [InlineData(67ul, StatType.IncreasedMeleeDamage, 5, 7, null, null)]
    public void Seed_each_linked_modifiers_ranges(ulong templateId, StatType stat, int min, int max, int? min2, int? max2)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        ItemTemplateModifier link = Assert.Single(context.ItemTemplateModifiers.AsNoTracking().ToList(),
            l => l.TemplateId.Value == templateId);
        ItemModifierStat line = context.ItemModifierStats.AsNoTracking().ToList()
            .Single(s => s.ModifierId == link.ModifierId && s.Stat == stat);

        Assert.Equal((min, max, min2, max2), (line.Min, line.Max, line.Min2, line.Max2));
    }

    /// <summary>
    /// #1179 (B2-R9, Review Focus 5): the 26 implicit groups, one stat each, with rows T9, T8 and T7 at item levels 1, 4
    /// and 7 and no weight, each row's range its source's at the same tier: the affix family of that stat, P7's for
    /// increased projectile and physical damage (which no family gives), maximum power its own 10-15 / 20-25 / 30-40. Every
    /// one of the 122 Common bases links exactly one implicit row, at its own tier (required level 1, 5 and 10: T9, T8 and
    /// T7); no unique links one (its copies roll its base's).
    /// </summary>
    [Fact]
    public void Seed_every_base_with_one_tier_matched_implicit()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var groups = context.ItemModifierGroups.AsNoTracking().ToList();
        var modifiers = context.ItemModifiers.AsNoTracking().AsSplitQuery()
            .Include(m => m.Stats).Include(m => m.Weights).ToList();
        var links = context.ItemTemplateModifiers.AsNoTracking().ToList();
        var templates = context.ItemTemplates.AsNoTracking().ToList();

        StatType StatOf(ItemModifierGroup group) => modifiers.First(m => m.GroupId == group.Id).Stats.Single().Stat;
        ItemModifier[] TiersOf(ItemModifierGroup group) => [.. modifiers.Where(m => m.GroupId == group.Id).OrderByDescending(m => m.Tier)];
        static (int, int, int?, int?) Range(ItemModifier row) =>
            row.Stats.Single() is var line ? (line.Min, line.Max, line.Min2, line.Max2) : default;

        ItemModifierGroup[] affixes = [.. groups.Where(g => g.Kind is ItemModifierKind.Prefix or ItemModifierKind.Suffix)];
        ItemModifierGroup[] implicits = [.. groups.Where(g => g.Kind == ItemModifierKind.Implicit)];
        Assert.Equal(26, implicits.Length);
        Assert.Equal(26, implicits.Select(StatOf).Distinct().Count());
        foreach (ItemModifierGroup group in implicits)
        {
            StatType stat = StatOf(group);
            ItemModifier[] rows = TiersOf(group);
            Assert.Equal([((byte?)9, (ushort)1), (8, 4), (7, 7)], rows.Select(m => (m.Tier, m.RequiredItemLevel)));
            Assert.All(rows, row => Assert.Equal(stat, row.Stats.Single().Stat));
            Assert.All(rows, row => Assert.Empty(row.Weights));

            IEnumerable<(int, int, int?, int?)> source = stat switch
            {
                StatType.Power => [(10, 15, null, null), (20, 25, null, null), (30, 40, null, null)],
                StatType.IncreasedProjectileDamage or StatType.IncreasedPhysicalDamage =>
                    TiersOf(affixes.Single(g => g.Code == "P7")).Select(Range),
                _ => TiersOf(affixes.Single(g => StatOf(g) == stat)).Select(Range),
            };
            Assert.Equal(source, rows.Select(Range));
        }

        var kindOf = modifiers.ToDictionary(m => m.Id, m => groups.Single(g => g.Id == m.GroupId).Kind);
        var tierOf = modifiers.ToDictionary(m => m.Id, m => m.Tier);
        ItemTemplate[] bases = [.. templates.Where(t => t.Rarity == ItemRarity.Common && t.BaseKind is not null)];
        Assert.Equal(122, bases.Length);
        Assert.All(bases, b =>
        {
            ItemTemplateModifier link = Assert.Single(links, l => l.TemplateId.Value == b.Id.Value);
            byte? tier = b.RequiredLevel switch { 1 => 9, 5 => 8, 10 => 7, _ => null };
            Assert.Equal((ItemModifierKind.Implicit, tier), (kindOf[link.ModifierId], tierOf[link.ModifierId]));
        });
        Assert.All(templates.Where(t => t.Rarity == ItemRarity.Unique), unique =>
            Assert.DoesNotContain(links, l => l.TemplateId.Value == unique.Id.Value && kindOf[l.ModifierId] == ItemModifierKind.Implicit));
    }

    /// <summary>#1179: a sample of the content table's implicits, one per kind of theme, by base.</summary>
    [Theory]
    [InlineData(36ul, "implicit FireResistance T9")]
    [InlineData(86ul, "implicit IncreasedMaximumLife T7")]
    [InlineData(49ul, "implicit IncreasedProjectileDamage T9")]
    [InlineData(43ul, "implicit Power T9")]
    [InlineData(70ul, "implicit Health T9")]
    [InlineData(75ul, "implicit IncreasedSpellDamage T7")]
    [InlineData(100ul, "implicit CritMultiplier T7")]
    [InlineData(35ul, "implicit IncreasedPhysicalDamage T9")]
    [InlineData(125ul, "implicit AddedLightningDamageToAttacks T8")]
    [InlineData(149ul, "implicit CooldownRecovery T7")]
    [InlineData(150ul, "implicit IncreasedHealing T9")]
    public void Give_each_base_the_implicit_the_table_sets(ulong template, string key)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        ItemTemplateModifier link = Assert.Single(context.ItemTemplateModifiers.AsNoTracking().ToList(),
            l => l.TemplateId.Value == template);
        Assert.Equal(CatalogKeys.ModifierId(key), link.ModifierId);
    }

    /// <summary>
    /// #1179 (B2-R10, B2-R11, B2-R13, Review Focus 4): rings come in nine lines and amulets in seven, each line three tiers
    /// at required levels 1, 5 and 10, priced 200, 400 and 800 (a quarter back), asking no attribute, named as the content
    /// table names them, in its order (the lines by their first tier's id); the trinket kind has two lines of one tier, the
    /// unique-only bases 150 and 151. Nothing hands a new base out: no loot entry, vendor row, quest reward or choice, or
    /// starting item names one of 104-151 (the loot tables keep naming the first tiers, 68 and 69, and a drop draws its
    /// line).
    /// </summary>
    [Fact]
    public async Task Seed_jewellery_as_lines_of_three_tiers()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var templates = context.ItemTemplates.AsNoTracking().ToList();
        var items = ItemsPatch.Build(templates, await new ItemModifierRepository(database,
            NullLoggerFactory.Instance).GetCatalogAsync(CancellationToken.None), NullLogger.Instance);

        string[][] rings =
        [
            ["Copper Band", "Oakbark Band", "Elmroot Band"],
            ["Thornwire Ring", "Briarsteel Ring", "Bramblefang Ring"],
            ["Emberknot Ring", "Cinderknot Ring", "Ashcoil Ring"],
            ["Hearthstone Ring", "Kilnstone Ring", "Forgeheart Ring"],
            ["Rimeglass Ring", "Frostglass Ring", "Winterglass Ring"],
            ["Lodestone Ring", "Stormlode Ring", "Thunderlode Ring"],
            ["Willowbark Ring", "Fenwillow Ring", "Elderwillow Ring"],
            ["Sparkthorn Ring", "Stormthorn Ring", "Thunderthorn Ring"],
            ["Frostbud Ring", "Rimebud Ring", "Winterbud Ring"],
        ];
        string[][] amulets =
        [
            ["Bone Charm", "Tusk Charm", "Horn Charm"],
            ["Antler Charm", "Hartsong Charm", "Stagcrown Charm"],
            ["Hollowfeather Charm", "Owlquill Charm", "Nightplume Charm"],
            ["Gloam Pendant", "Mirk Pendant", "Nightmire Pendant"],
            ["Mossheart Pendant", "Fernheart Pendant", "Elderheart Pendant"],
            ["Foxtooth Pendant", "Wolfstooth Pendant", "Bearclaw Pendant"],
            ["Dewdrop Pendant", "Rainbead Pendant", "Wellspring Pendant"],
        ];
        Assert.Equal(rings, items.LinesOf(ItemBaseKind.Ring).Select(line => line.Select(t => t.Name).ToArray()));
        Assert.Equal(amulets, items.LinesOf(ItemBaseKind.Amulet).Select(line => line.Select(t => t.Name).ToArray()));
        Assert.All(items.LinesOf(ItemBaseKind.Ring).Concat(items.LinesOf(ItemBaseKind.Amulet)), line => Assert.Equal(
            [((ushort?)1, 200u, 50u), (5, 400u, 100u), (10, 800u, 200u)],
            line.Select(t => (t.RequiredLevel, t.BuyPrice, t.SellPrice))));
        Assert.All(templates.Where(t => t.BaseKind is ItemBaseKind.Ring or ItemBaseKind.Amulet or ItemBaseKind.Trinket), t =>
            Assert.Equal((ushort?)null, t.RequiredStrength ?? t.RequiredAgility ?? t.RequiredIntellect));
        Assert.Equal([[150ul], [151ul]], items.LinesOf(ItemBaseKind.Trinket).Select(line => line.Select(t => t.Id.Value)));

        IEnumerable<ulong> named = context.LootTableEntries.AsNoTracking().ToList()
            .Where(e => e.ItemTemplateId is not null).Select(e => e.ItemTemplateId!.Value)
            .Concat(context.VendorStocks.AsNoTracking().ToList().Select(v => v.ItemTemplateId.Value))
            .Concat(context.QuestItemRewards.AsNoTracking().ToList().Select(r => r.ItemTemplateId.Value))
            .Concat(context.QuestItemChoices.AsNoTracking().ToList().Select(c => c.ItemTemplateId.Value))
            .Concat(context.CharacterCreateInfos.AsNoTracking().ToList().SelectMany(c => c.StartingItems).Select(i => i.Value));
        Assert.DoesNotContain(named, id => id is >= 104 and <= 151);
    }

    /// <summary>
    /// #1179 (B2-R12): each unique names its base, a Common base of its own kind at or under its required level, whose
    /// base stats and implicit its copies roll; the unique carries no base-stat row of its own.
    /// </summary>
    [Theory]
    [InlineData(58ul, 144ul)]
    [InlineData(64ul, 112ul)]
    [InlineData(65ul, 125ul)]
    [InlineData(66ul, 150ul)]
    [InlineData(67ul, 151ul)]
    public void Seed_each_unique_on_its_named_base(ulong unique, ulong @base)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var templates = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(t => t.Id.Value);

        ItemTemplate made = templates[unique], named = templates[@base];
        Assert.Equal(@base, made.BaseTemplateId!.Value);
        Assert.Equal((ItemRarity.Common, made.BaseKind), (named.Rarity, named.BaseKind));
        Assert.True(named.RequiredLevel <= made.RequiredLevel, $"{named.Name} asks a higher level than {made.Name}");
        Assert.Empty(made.BaseStats);
    }

    /// <summary>
    /// #1179 (decisions 12-16, Review Focus 5): one full best-in-slot level-10 set stays within the owner's ceilings,
    /// worked out from the seed the way the content table's budget check counts it. Every slot (head, shoulders, chest,
    /// hands, legs, feet, amulet, two rings, two trinkets, and the hands' best of a one-handed weapon with an off-hand or a
    /// two-handed weapon) holds whichever item gives the budget most: a base as a rare (its implicit, plus each affix group
    /// of the stat that rolls on its kind, at its best tier), or a unique (its base's implicit plus its own lines), each
    /// line at its maximum (an "adds" line's high end). Attack speed counts local and global attack speed and 0.1 % per
    /// Agility; life is (a class's level-10 base + flat life + Stamina's life) times (1 + increased maximum life / 100),
    /// rounded half away from zero as the stats fold does, and must land 40-60 % above that class's base.
    /// </summary>
    [Theory]
    [InlineData("FireResistance", 0, 25)]
    [InlineData("ColdResistance", 0, 25)]
    [InlineData("LightningResistance", 0, 25)]
    [InlineData("BlightResistance", 0, 25)]
    [InlineData("attack speed", 0, 10)]
    [InlineData("CastSpeed", 0, 10)]
    [InlineData("MovementSpeed", 0, 8)]
    [InlineData("CritPct", 0, 6)]
    [InlineData("life Warrior", 40, 60)]
    [InlineData("life Hunter", 40, 60)]
    [InlineData("life Cleric", 40, 60)]
    [InlineData("life Wizard", 40, 60)]
    public void Fit_the_starting_zone_budget(string budget, double floor, double ceiling)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var templates = context.ItemTemplates.AsNoTracking().ToList().ToDictionary(t => t.Id.Value);
        var modifiers = context.ItemModifiers.AsNoTracking().AsSplitQuery()
            .Include(m => m.Group).Include(m => m.Stats).Include(m => m.Weights).ToList();
        ILookup<ulong, ItemModifier> linked = context.ItemTemplateModifiers.AsNoTracking().ToList()
            .ToLookup(l => l.TemplateId.Value, l => modifiers.Single(m => m.Id == l.ModifierId));
        ItemModifier[] affixes = [.. modifiers.Where(m => m.Group.Kind is ItemModifierKind.Prefix or ItemModifierKind.Suffix)];
        uint lifePerStamina = context.CombatFormulas.AsNoTracking().Single().LifePerStamina;

        double total = budget.Split(' ') switch
        {
            ["attack", "speed"] =>
                Worst((StatType.AttackSpeed, 1), (StatType.LocalAttackSpeed, 1), (StatType.Agility, 0.1)),
            ["life", var name] => LifeShare(Enum.Parse<CharacterClass>(name)),
            _ => Worst((Enum.Parse<StatType>(budget), 1)),
        };

        Assert.InRange(total, floor, ceiling);

        // The share a full set adds to a class's level-10 life, in percent.
        double LifeShare(CharacterClass c)
        {
            ClassLevelStat row = context.ClassLevelStats.AsNoTracking().Single(s => s.Class == c && s.Level == 10);
            long life = row.BaseHp + (row.Stamina * lifePerStamina);
            long flat = (long)Worst((StatType.Health, 1), (StatType.Stamina, lifePerStamina));
            long increased = (long)Worst((StatType.IncreasedMaximumLife, 1));
            long withGear = (long)Math.Round((life + flat) * (100 + increased) / 100m, MidpointRounding.AwayFromZero);
            return 100.0 * (withGear - life) / life;
        }

        // The weighted stats' most over every slot of a level-10 set.
        double Worst(params (StatType Stat, double Weight)[] stats)
        {
            double Lines(IEnumerable<ItemModifier> rows) => rows.SelectMany(m => m.Stats)
                .Sum(line => stats.Where(s => s.Stat == line.Stat).Sum(s => s.Weight * (line.Max2 ?? line.Max)));

            // What one item gives at its best: a unique its base's implicit and its own lines; a base its implicit and,
            // rolled rare, each affix group of these stats on its kind at the group's best tier.
            double Gives(ItemTemplate item) => item.Rarity == ItemRarity.Unique
                ? Lines(linked[item.BaseTemplateId!.Value]) + Lines(linked[item.Id.Value])
                : Lines(linked[item.Id.Value]) + affixes
                    .Where(m => m.RequiredItemLevel <= 10 && m.Weights.Any(w => w.BaseKind == item.BaseKind && w.Weight > 0))
                    .GroupBy(m => m.GroupId)
                    .Sum(group => group.Max(m => Lines([m])));

            ItemTemplate[] wearable =
            [
                .. templates.Values.Where(t => t.BaseKind is not null && t.RequiredLevel <= 10
                                               && t.Rarity is ItemRarity.Common or ItemRarity.Unique),
            ];
            double Best(Func<ItemTemplate, bool> fits) => wearable.Where(fits).Select(Gives).DefaultIfEmpty(0).Max();
            bool In(ItemTemplate item, ItemSlotType slot) => ItemBaseKindSlots.SlotOf(item.BaseKind!.Value) == slot;

            ItemSlotType[] once = [ItemSlotType.Head, ItemSlotType.Shoulder, ItemSlotType.Chest, ItemSlotType.Hands,
                ItemSlotType.Legs, ItemSlotType.Feet, ItemSlotType.Neck];
            ItemSlotType[] twice = [ItemSlotType.Finger, ItemSlotType.Trinket];
            double hands = Math.Max(
                Best(i => In(i, ItemSlotType.MainHand) && !i.IsTwoHanded) + Best(i => In(i, ItemSlotType.OffHand)),
                Best(i => In(i, ItemSlotType.MainHand) && i.IsTwoHanded));
            return once.Sum(slot => Best(i => In(i, slot))) + twice.Sum(slot => 2 * Best(i => In(i, slot))) + hands;
        }
    }

    /// <summary>
    /// #1179: every catalog row keeps the id it was seeded with, since items store it: ids are literal in the seed and
    /// unique, and each row's key (its family, element and tier, or its unique's or base's template) keeps the group id
    /// and modifier id committed in item-modifier-ids.tsv. A new row adds a line; an existing line never changes.
    /// </summary>
    [Fact]
    public void Keep_every_catalog_row_at_its_committed_id()
    {
        Assert.Equal(ItemModifierSeed.Groups().Length, ItemModifierSeed.Groups().Select(g => g.Id).Distinct().Count());
        Assert.Equal(ItemModifierSeed.Modifiers().Length, ItemModifierSeed.Modifiers().Select(m => m.Id).Distinct().Count());

        string path = Path.Combine(RepositoryLayout.Root(), "tests", "Avalon.Server.World.UnitTests", "Seeding", "item-modifier-ids.tsv");
        string[] committed =
        [
            .. File.ReadAllLines(path).Where(line => line.Length > 0 && !line.StartsWith('#')).Order(StringComparer.Ordinal),
        ];
        string[] seeded =
        [
            .. CatalogKeys.All().Select(row => $"{row.Key}\t{row.GroupId}\t{row.ModifierId}").Order(StringComparer.Ordinal),
        ];

        Assert.Equal(committed, seeded);
    }

    /// <summary>
    /// #1179: a base's defence and a weapon's damage and attack time are its base stats. Its drafted values
    /// (<paramref name="drafted" />, in ItemBaseStat order) are ItemBaseSeed's, and each is seeded as that value ±20 %
    /// (an attack time ±5 %), rounded half away from zero and at least 1.
    /// </summary>
    private void AssertDrafted(ulong templateId, params (ItemBaseStat Stat, int Value)[] drafted)
    {
        static int Spread(int value, int percent) =>
            Math.Max(1, (int)Math.Round(value * (100 + percent) / 100m, MidpointRounding.AwayFromZero));
        static int Percent(ItemBaseStat stat) => stat == ItemBaseStat.AttackTime ? 5 : 20;

        Assert.Equal(drafted, ItemBaseSeed.Drafted().Where(d => d.TemplateId == templateId).Select(d => (d.Stat, d.Value)));
        Assert.Equal(
            drafted.Select(line => (line.Stat, Spread(line.Value, -Percent(line.Stat)), Spread(line.Value, Percent(line.Stat)))),
            baseStatTables.Ranges[templateId].OrderBy(s => s.Stat).Select(s => (s.Stat, s.Min, s.Max)));
    }
}
