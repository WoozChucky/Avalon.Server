using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Database.World.Seeding;

/// <summary>A base's implicit stat as drafted (#1179): the value its template carried, which the seeded range spreads.</summary>
public readonly record struct DraftedImplicit(ulong TemplateId, StatType Stat, int Value);

/// <summary>
/// The seeded modifier catalog (#1179), built from the compact tables below: the affix families and their tiers, the five
/// uniques and the bases' drafted implicits. The model's HasData, the catalog exports, the doc page and the tests read
/// it from here.
/// <list type="bullet">
/// <item>Every element of a family split by element (P8, P9, P10, S5) is its own group; a unique is one group, and each
/// base's implicits are one group named after the base.</item>
/// <item>Ids are literal surrogates written in the tables: every group and every modifier row carries its own id, which
/// means nothing and never changes, since items store the modifier id. A row added later takes the next free id, wherever
/// it sits in the tables; SeedIntegrityShould holds every existing row to its committed id.</item>
/// <item>Tiers unlock at item level 1, 4, 7, 10 and 13 and weigh 1000, 800, 600, 400 and 200 on every base kind their
/// family rolls on, and on no other. An "adds X-Y" line seeds its low end [X, m] and its high end [m, Y], with m the
/// floor of their mean.</item>
/// <item>A unique's lines are its drafted values ±20 %, an implicit's ±15 %, rounded half away from zero and at least 1;
/// neither has a tier, a weight or an item-level gate.</item>
/// </list>
/// </summary>
public static class ItemModifierSeed
{
    private const int UniqueSpreadPercent = 20;
    private const int ImplicitSpreadPercent = 15;

    private static readonly ushort[] s_tierLevels = [1, 4, 7, 10, 13];
    private static readonly int[] s_tierWeights = [1000, 800, 600, 400, 200];

    private static readonly ItemBaseKind[] s_plate =
    [
        ItemBaseKind.PlateHead, ItemBaseKind.PlateShoulders, ItemBaseKind.PlateChest, ItemBaseKind.PlateHands,
        ItemBaseKind.PlateLegs, ItemBaseKind.PlateFeet,
    ];

    private static readonly ItemBaseKind[] s_leather =
    [
        ItemBaseKind.LeatherHead, ItemBaseKind.LeatherShoulders, ItemBaseKind.LeatherChest, ItemBaseKind.LeatherHands,
        ItemBaseKind.LeatherLegs, ItemBaseKind.LeatherFeet,
    ];

    private static readonly ItemBaseKind[] s_cloth =
    [
        ItemBaseKind.ClothHead, ItemBaseKind.ClothShoulders, ItemBaseKind.ClothChest, ItemBaseKind.ClothHands,
        ItemBaseKind.ClothLegs, ItemBaseKind.ClothFeet,
    ];

    private static readonly ItemBaseKind[] s_armour = [.. s_plate, .. s_leather, .. s_cloth];
    private static readonly ItemBaseKind[] s_gloves = [ItemBaseKind.PlateHands, ItemBaseKind.LeatherHands, ItemBaseKind.ClothHands];
    private static readonly ItemBaseKind[] s_helmets = [ItemBaseKind.PlateHead, ItemBaseKind.LeatherHead, ItemBaseKind.ClothHead];
    private static readonly ItemBaseKind[] s_shoulders = [ItemBaseKind.PlateShoulders, ItemBaseKind.LeatherShoulders, ItemBaseKind.ClothShoulders];
    private static readonly ItemBaseKind[] s_boots = [ItemBaseKind.PlateFeet, ItemBaseKind.LeatherFeet, ItemBaseKind.ClothFeet];
    private static readonly ItemBaseKind[] s_weapons = [ItemBaseKind.Sword, ItemBaseKind.Mace, ItemBaseKind.Staff, ItemBaseKind.Bow];

    private const ItemBaseKind Sword = ItemBaseKind.Sword;
    private const ItemBaseKind Mace = ItemBaseKind.Mace;
    private const ItemBaseKind Staff = ItemBaseKind.Staff;
    private const ItemBaseKind Bow = ItemBaseKind.Bow;
    private const ItemBaseKind Shield = ItemBaseKind.Shield;
    private const ItemBaseKind CasterOffHand = ItemBaseKind.CasterOffHand;
    private const ItemBaseKind Amulet = ItemBaseKind.Amulet;
    private const ItemBaseKind Ring = ItemBaseKind.Ring;

    // The tier ranges shared by several families: the percentage families, the attributes and the global added damage.
    private static readonly (int, int)[] s_increasedLocal = [(10, 19), (20, 34), (35, 49), (50, 64), (65, 79)];
    private static readonly (int, int)[] s_attribute = [(1, 3), (4, 6), (7, 10), (11, 15), (16, 20)];
    private static readonly (int, int)[] s_addedGlobal = [(1, 2), (1, 3), (2, 4), (2, 5), (3, 6)];
    private static readonly (int, int)[] s_overTime = [(6, 10), (11, 16), (17, 22), (23, 28), (29, 34)];

    /// <summary>The catalog groups, each with its literal id: affix families, then uniques, then implicits.</summary>
    public static ItemModifierGroup[] Groups() => Build().Groups;

    /// <summary>The catalog rows, without navigations (HasData), each with its literal id.</summary>
    public static ItemModifier[] Modifiers() => Build().Modifiers;

    /// <summary>Every modifier's stat lines.</summary>
    public static ItemModifierStat[] Stats() => Build().Stats;

    /// <summary>Every prefix and suffix row's weight on each base kind it rolls on.</summary>
    public static ItemModifierWeight[] Weights() => Build().Weights;

    /// <summary>The template links: each unique's own modifier and each base's implicit group's row.</summary>
    public static ItemTemplateModifier[] TemplateLinks() => Build().Links;

    /// <summary>The bases' drafted implicit stats by template id, in each base's line order.</summary>
    public static DraftedImplicit[] Implicits() =>
    [
        .. s_implicits.OrderBy(i => i.Template)
            .SelectMany(i => i.Lines.Select(line => new DraftedImplicit(i.Template, line.Stat, line.Value))),
    ];

    /// <summary>One group of a family: its id, element, name and stat, and its rows' ids by tier.</summary>
    private sealed record FamilyGroup(int Id, DamageType? Element, string Name, StatType Stat, int[] Rows);

    private sealed record Family(
        string Code, ItemModifierKind Kind, bool Adds, (int Low, int High)[] Tiers, ItemBaseKind[] RollsOn, FamilyGroup[] Groups);

    private sealed record Built(
        ItemModifierGroup[] Groups, ItemModifier[] Modifiers, ItemModifierStat[] Stats, ItemModifierWeight[] Weights,
        ItemTemplateModifier[] Links);

    private static Family Prefix(string code, int group, int[] rows, ItemBaseKind[] rollsOn, (int, int)[] tiers, string name,
        StatType stat, bool adds = false) =>
        new(code, ItemModifierKind.Prefix, adds, tiers, rollsOn, [new(group, null, name, stat, rows)]);

    private static Family Suffix(string code, int group, int[] rows, ItemBaseKind[] rollsOn, (int, int)[] tiers, string name,
        StatType stat) =>
        new(code, ItemModifierKind.Suffix, false, tiers, rollsOn, [new(group, null, name, stat, rows)]);

    private static (int, int)[] Single(params int[] values) => [.. values.Select(value => (value, value))];

    private static readonly Family[] s_families =
    [
        Prefix("P1", 1, [1, 2, 3, 4, 5], s_weapons, [(1, 2), (2, 4), (3, 6), (5, 8), (6, 11)], "Briar-edged", StatType.AddedPhysicalDamage, adds: true),
        Prefix("P2", 2, [6, 7, 8, 9, 10], s_weapons, [(1, 3), (2, 5), (3, 7), (4, 10), (6, 13)], "Smouldering", StatType.AddedFireDamage, adds: true),
        Prefix("P3", 3, [11, 12, 13, 14, 15], s_weapons, [(1, 2), (2, 4), (3, 6), (4, 8), (5, 11)], "Rimed", StatType.AddedColdDamage, adds: true),
        Prefix("P4", 4, [16, 17, 18, 19, 20], s_weapons, [(1, 4), (1, 7), (1, 10), (2, 14), (2, 18)], "Crackling", StatType.AddedLightningDamage, adds: true),
        Prefix("P5", 5, [21, 22, 23, 24, 25], s_weapons, [(1, 2), (2, 4), (3, 6), (4, 8), (5, 11)], "Festering", StatType.AddedBlightDamage, adds: true),
        Prefix("P6", 6, [26, 27, 28, 29, 30], s_weapons, s_increasedLocal, "Honed", StatType.LocalIncreasedPhysicalDamage),
        Prefix("P7", 7, [31, 32, 33, 34, 35], [Mace, Staff, CasterOffHand], [(8, 12), (13, 20), (21, 30), (31, 40), (41, 50)], "Whispering", StatType.IncreasedSpellDamage),
        new("P8", ItemModifierKind.Prefix, false, [(5, 9), (10, 14), (15, 19), (20, 24), (25, 29)], [Staff, CasterOffHand, Amulet, Ring],
        [
            new(8, DamageType.Fire, "Ashen", StatType.IncreasedFireDamage, [36, 37, 38, 39, 40]),
            new(9, DamageType.Cold, "Hoarfrost", StatType.IncreasedColdDamage, [41, 42, 43, 44, 45]),
            new(10, DamageType.Lightning, "Galvanic", StatType.IncreasedLightningDamage, [46, 47, 48, 49, 50]),
            new(11, DamageType.Blight, "Mouldering", StatType.IncreasedBlightDamage, [51, 52, 53, 54, 55]),
        ]),
        new("P9", ItemModifierKind.Prefix, true, s_addedGlobal, [Ring, Amulet, .. s_gloves],
        [
            new(12, DamageType.Physical, "Thorn-tipped", StatType.AddedPhysicalDamageToAttacks, [56, 57, 58, 59, 60]),
            new(13, DamageType.Fire, "Thorn-tipped", StatType.AddedFireDamageToAttacks, [61, 62, 63, 64, 65]),
            new(14, DamageType.Cold, "Thorn-tipped", StatType.AddedColdDamageToAttacks, [66, 67, 68, 69, 70]),
            new(15, DamageType.Lightning, "Thorn-tipped", StatType.AddedLightningDamageToAttacks, [71, 72, 73, 74, 75]),
            new(16, DamageType.Blight, "Thorn-tipped", StatType.AddedBlightDamageToAttacks, [76, 77, 78, 79, 80]),
        ]),
        new("P10", ItemModifierKind.Prefix, true, s_addedGlobal, [Ring, Amulet, CasterOffHand],
        [
            new(17, DamageType.Fire, "Ember-sung", StatType.AddedFireDamageToSpells, [81, 82, 83, 84, 85]),
            new(18, DamageType.Cold, "Ember-sung", StatType.AddedColdDamageToSpells, [86, 87, 88, 89, 90]),
            new(19, DamageType.Lightning, "Ember-sung", StatType.AddedLightningDamageToSpells, [91, 92, 93, 94, 95]),
        ]),
        Prefix("P11", 20, [96, 97, 98, 99, 100], [.. s_armour, Shield, Ring, Amulet], [(5, 9), (10, 19), (20, 29), (30, 44), (45, 59)], "Hale", StatType.Health),
        Prefix("P12", 21, [101, 102, 103, 104, 105], [.. s_plate, Shield], [(2, 4), (4, 8), (8, 14), (14, 22), (22, 32)], "Barkbound", StatType.Armor),
        Prefix("P13", 22, [106, 107, 108, 109, 110], [.. s_plate, Shield], s_increasedLocal, "Ironbark", StatType.LocalIncreasedArmor),
        Prefix("P14", 23, [111, 112, 113, 114, 115], s_leather, [(10, 19), (20, 39), (40, 64), (65, 94), (95, 129)], "Fleet-footed", StatType.Evasion),
        Prefix("P15", 24, [116, 117, 118, 119, 120], s_leather, s_increasedLocal, "Mistwoven", StatType.LocalIncreasedEvasion),
        Prefix("P16", 25, [121, 122, 123, 124, 125], [.. s_cloth, CasterOffHand], [(3, 5), (6, 10), (11, 17), (18, 26), (27, 37)], "Veilwoven", StatType.Ward),
        Prefix("P17", 26, [126, 127, 128, 129, 130], [.. s_cloth, CasterOffHand], s_increasedLocal, "Glamoured", StatType.LocalIncreasedWard),
        Prefix("P18", 27, [131, 132, 133, 134, 135], [Amulet, Staff, CasterOffHand], s_overTime, "Lingering", StatType.IncreasedDamageOverTime),
        Prefix("P19", 28, [136, 137, 138, 139, 140], [Mace, Staff, CasterOffHand, Amulet], s_overTime, "Mending", StatType.IncreasedHealing),

        Suffix("S1", 29, [141, 142, 143, 144, 145], [.. s_armour, Sword, Mace, Bow, Shield, Ring, Amulet], s_attribute, "of the Ox-root", StatType.Strength),
        Suffix("S2", 30, [146, 147, 148, 149, 150], [.. s_armour, Sword, Staff, Bow, Ring, Amulet], s_attribute, "of the Hare", StatType.Agility),
        Suffix("S3", 31, [151, 152, 153, 154, 155], [.. s_armour, Sword, Mace, Staff, CasterOffHand, Ring, Amulet], s_attribute, "of the Owl", StatType.Intellect),
        Suffix("S4", 32, [156, 157, 158, 159, 160], [.. s_armour, Shield, Ring, Amulet], s_attribute, "of the Old Oak", StatType.Stamina),
        new("S5", ItemModifierKind.Suffix, false, [(6, 11), (12, 17), (18, 23), (24, 29), (30, 35)], [.. s_armour, Shield, Ring, Amulet],
        [
            new(33, DamageType.Fire, "of the Hearth", StatType.FireResistance, [161, 162, 163, 164, 165]),
            new(34, DamageType.Cold, "of the Thaw", StatType.ColdResistance, [166, 167, 168, 169, 170]),
            new(35, DamageType.Lightning, "of Grounding", StatType.LightningResistance, [171, 172, 173, 174, 175]),
            new(36, DamageType.Blight, "of the Clean Spring", StatType.BlightResistance, [176, 177, 178, 179, 180]),
        ]),
        Suffix("S6", 37, [181, 182, 183, 184, 185], s_weapons, [(5, 7), (8, 10), (11, 13), (14, 16), (17, 19)], "of Haste", StatType.LocalAttackSpeed),
        Suffix("S7", 38, [186, 187, 188, 189], [.. s_gloves, Ring], [(3, 4), (5, 6), (7, 8), (9, 10)], "of Quick Hands", StatType.AttackSpeed),
        Suffix("S8", 39, [190, 191, 192, 193, 194], [Staff, Mace, Amulet, Ring, CasterOffHand], [(4, 6), (7, 9), (10, 12), (13, 15), (16, 18)], "of Cadence", StatType.CastSpeed),
        Suffix("S9", 40, [195, 196, 197, 198, 199], [.. s_weapons, CasterOffHand, Amulet, Ring], Single(1, 2, 3, 4, 5), "of the Keen Eye", StatType.CritPct),
        Suffix("S10", 41, [200, 201, 202, 203, 204], [.. s_weapons, Amulet], [(8, 12), (13, 17), (18, 22), (23, 27), (28, 32)], "of Ruin", StatType.CritMultiplier),
        Suffix("S11", 42, [205, 206, 207, 208, 209], s_boots, Single(5, 8, 11, 14, 17), "of the Wanderer", StatType.MovementSpeed),
        Suffix("S12", 43, [210, 211, 212, 213, 214], [.. s_helmets, .. s_shoulders, Amulet], [(3, 4), (5, 6), (7, 8), (9, 10), (11, 12)], "of Renewal", StatType.CooldownRecovery),
        Suffix("S13", 44, [215, 216, 217, 218], [Shield], [(3, 4), (5, 6), (7, 8), (9, 10)], "of the Bulwark", StatType.BlockPct),
    ];

    // The uniques: group id, modifier id, template, and the drafted lines (a stat and its value, and an "adds" line's
    // high end).
    private static readonly (int Group, int Row, ulong Template, (StatType Stat, int Value, int? High)[] Lines)[] s_uniques =
    [
        (45, 219, 58, [(StatType.Health, 12, null), (StatType.Strength, 3, null), (StatType.Agility, 3, null), (StatType.IncreasedAttackDamage, 8, null)]),
        (46, 220, 64, [(StatType.Health, 20, null), (StatType.BlightResistance, 12, null), (StatType.IncreasedHealing, 8, null)]),
        (47, 221, 65, [(StatType.Health, 30, null), (StatType.AddedPhysicalDamageToAttacks, 2, 4), (StatType.CritPct, 4, null)]),
        (48, 222, 66, [(StatType.Health, 25, null), (StatType.IncreasedDamageOverTime, 15, null), (StatType.BlightResistance, 10, null)]),
        (49, 223, 67, [(StatType.Health, 20, null), (StatType.AttackSpeed, 6, null), (StatType.CastSpeed, 6, null)]),
    ];

    private static (int Group, int Row, ulong Template, (StatType Stat, int Value)[] Lines) Base(
        int group, int row, ulong template, params (StatType Stat, int Value)[] lines) => (group, row, template, lines);

    // The bases' implicits: group id, modifier id, template, and the drafted lines, the values the bases' templates carry
    // (an armour piece's defence, a shield's armour and block, a caster off-hand's ward and increased spell damage).
    private static readonly (int Group, int Row, ulong Template, (StatType Stat, int Value)[] Lines)[] s_implicits =
    [
        Base(50, 224, 12, (StatType.Armor, 11)),
        Base(51, 225, 13, (StatType.Armor, 18)),
        Base(52, 226, 14, (StatType.Armor, 14)),
        Base(53, 227, 15, (StatType.Armor, 7)),
        Base(54, 228, 16, (StatType.Armor, 7)),
        Base(55, 229, 17, (StatType.Ward, 16)),
        Base(56, 230, 18, (StatType.Ward, 26)),
        Base(57, 231, 19, (StatType.Ward, 21)),
        Base(58, 232, 20, (StatType.Ward, 10)),
        Base(59, 233, 21, (StatType.Ward, 10)),
        Base(60, 234, 22, (StatType.Evasion, 54)),
        Base(61, 235, 23, (StatType.Evasion, 90)),
        Base(62, 236, 24, (StatType.Evasion, 72)),
        Base(63, 237, 25, (StatType.Evasion, 36)),
        Base(64, 238, 26, (StatType.Evasion, 36)),
        Base(65, 239, 36, (StatType.Armor, 5)),
        Base(66, 240, 37, (StatType.Armor, 8)),
        Base(67, 241, 38, (StatType.Armor, 6)),
        Base(68, 242, 39, (StatType.Armor, 3)),
        Base(69, 243, 40, (StatType.Armor, 3)),
        Base(70, 244, 41, (StatType.Ward, 7)),
        Base(71, 245, 42, (StatType.Ward, 12)),
        Base(72, 246, 43, (StatType.Ward, 10)),
        Base(73, 247, 44, (StatType.Ward, 5)),
        Base(74, 248, 45, (StatType.Ward, 5)),
        Base(75, 249, 46, (StatType.Evasion, 24)),
        Base(76, 250, 47, (StatType.Evasion, 40)),
        Base(77, 251, 48, (StatType.Evasion, 32)),
        Base(78, 252, 49, (StatType.Evasion, 16)),
        Base(79, 253, 50, (StatType.Evasion, 16)),
        Base(80, 254, 70, (StatType.Armor, 6), (StatType.BlockPct, 5)),
        Base(81, 255, 71, (StatType.Armor, 14), (StatType.BlockPct, 7)),
        Base(82, 256, 72, (StatType.Armor, 24), (StatType.BlockPct, 10)),
        Base(83, 257, 73, (StatType.Ward, 8), (StatType.IncreasedSpellDamage, 5)),
        Base(84, 258, 74, (StatType.Ward, 18), (StatType.IncreasedSpellDamage, 8)),
        Base(85, 259, 75, (StatType.Ward, 30), (StatType.IncreasedSpellDamage, 12)),
        Base(86, 260, 76, (StatType.Armor, 4)),
        Base(87, 261, 77, (StatType.Evasion, 20)),
        Base(88, 262, 78, (StatType.Ward, 6)),
        Base(89, 263, 79, (StatType.Armor, 9)),
        Base(90, 264, 80, (StatType.Evasion, 45)),
        Base(91, 265, 81, (StatType.Ward, 13)),
        Base(92, 266, 82, (StatType.Armor, 16)),
        Base(93, 267, 83, (StatType.Evasion, 80)),
        Base(94, 268, 84, (StatType.Ward, 23)),
        Base(95, 269, 85, (StatType.Armor, 19)),
        Base(96, 270, 86, (StatType.Armor, 32)),
        Base(97, 271, 87, (StatType.Armor, 26)),
        Base(98, 272, 88, (StatType.Armor, 13)),
        Base(99, 273, 89, (StatType.Armor, 13)),
        Base(100, 274, 90, (StatType.Evasion, 96)),
        Base(101, 275, 91, (StatType.Evasion, 160)),
        Base(102, 276, 92, (StatType.Evasion, 128)),
        Base(103, 277, 93, (StatType.Evasion, 64)),
        Base(104, 278, 94, (StatType.Evasion, 64)),
        Base(105, 279, 95, (StatType.Ward, 27)),
        Base(106, 280, 96, (StatType.Ward, 45)),
        Base(107, 281, 97, (StatType.Ward, 36)),
        Base(108, 282, 98, (StatType.Ward, 18)),
        Base(109, 283, 99, (StatType.Ward, 18)),
    ];

    private static Built Build()
    {
        Dictionary<ulong, string> names = ItemBaseSeed.Equipment().ToDictionary(t => t.Id.Value, t => t.Name);
        List<ItemModifierGroup> groups = [];
        List<ItemModifier> modifiers = [];
        List<ItemModifierStat> stats = [];
        List<ItemModifierWeight> weights = [];
        List<ItemTemplateModifier> links = [];

        void AddGroup(int id, ItemModifierKind kind, string? code, DamageType? element, string name) =>
            groups.Add(new ItemModifierGroup { Id = id, Kind = kind, Code = code, Element = element, Name = name });

        void AddModifier(int id, int group, byte? tier, ushort requiredItemLevel) =>
            modifiers.Add(new ItemModifier { Id = id, GroupId = group, Tier = tier, RequiredItemLevel = requiredItemLevel });

        foreach (Family family in s_families)
        {
            foreach (FamilyGroup group in family.Groups)
            {
                if (group.Rows.Length != family.Tiers.Length)
                    throw new InvalidOperationException($"{family.Code} {group.Element} names {group.Rows.Length} row ids for {family.Tiers.Length} tiers.");

                AddGroup(group.Id, family.Kind, family.Code, group.Element, group.Name);
                for (int tier = 0; tier < family.Tiers.Length; tier++)
                {
                    int id = group.Rows[tier];
                    AddModifier(id, group.Id, (byte)(tier + 1), s_tierLevels[tier]);
                    (int low, int high) = family.Tiers[tier];
                    stats.Add(family.Adds
                        ? Line(id, 0, group.Stat, (low, (low + high) / 2), ((low + high) / 2, high))
                        : Line(id, 0, group.Stat, (low, high), null));
                    weights.AddRange(family.RollsOn.Order().Select(kind =>
                        new ItemModifierWeight { ModifierId = id, BaseKind = kind, Weight = s_tierWeights[tier] }));
                }
            }
        }

        foreach ((int group, int row, ulong template, (StatType Stat, int Value, int? High)[] lines) in s_uniques)
        {
            AddGroup(group, ItemModifierKind.Unique, null, null, names[template]);
            AddModifier(row, group, null, 1);
            for (int index = 0; index < lines.Length; index++)
            {
                (StatType stat, int value, int? high) = lines[index];
                stats.Add(Line(row, index, stat, Spread(value, UniqueSpreadPercent),
                    high is { } h ? Spread(h, UniqueSpreadPercent) : null));
            }

            links.Add(new ItemTemplateModifier { TemplateId = template, Index = 0, ModifierId = row });
        }

        foreach ((int group, int row, ulong template, (StatType Stat, int Value)[] lines) in s_implicits)
        {
            AddGroup(group, ItemModifierKind.Implicit, null, null, names[template]);
            AddModifier(row, group, null, 1);
            for (int index = 0; index < lines.Length; index++)
                stats.Add(Line(row, index, lines[index].Stat, Spread(lines[index].Value, ImplicitSpreadPercent), null));

            links.Add(new ItemTemplateModifier { TemplateId = template, Index = 0, ModifierId = row });
        }

        return new Built(
            [.. groups.OrderBy(g => g.Id)], [.. modifiers.OrderBy(m => m.Id)], [.. stats], [.. weights], [.. links]);
    }

    private static ItemModifierStat Line(int modifierId, int index, StatType stat, (int Min, int Max) range,
        (int Min, int Max)? second) => new()
        {
            ModifierId = modifierId,
            Index = (byte)index,
            Stat = stat,
            Min = range.Min,
            Max = range.Max,
            Min2 = second?.Min,
            Max2 = second?.Max,
        };

    /// <summary><paramref name="value" /> ± <paramref name="percent" /> %, each end rounded half away from zero and at least 1.</summary>
    private static (int Min, int Max) Spread(int value, int percent) =>
        (Math.Max(1, RoundHundredths(value * (100 - percent))), Math.Max(1, RoundHundredths(value * (100 + percent))));

    // A non-negative number of hundredths to the nearest whole, half away from zero, in integers so no binary fraction
    // decides a tie (25.5 is 26).
    private static int RoundHundredths(int hundredths) => ((2 * hundredths) + 100) / 200;
}
