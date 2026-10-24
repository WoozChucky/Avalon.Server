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
/// <item>Ids are plain sequential surrogates assigned here once, in a stable order, and never derived from position
/// afterwards: groups are the prefixes P1-P19 and the suffixes S1-S13 (a split code's elements in DamageType order), then
/// the uniques, then the implicit groups by base template id; modifiers follow their group and then their tier. A row
/// added later takes the next free id.</item>
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

    /// <summary>The catalog groups, ids 1 onward: affix families, then uniques, then implicits.</summary>
    public static ItemModifierGroup[] Groups() => Build().Groups;

    /// <summary>The catalog rows, without navigations (HasData), ids 1 onward by group and tier.</summary>
    public static ItemModifier[] Modifiers() => Build().Modifiers;

    /// <summary>Every modifier's stat lines.</summary>
    public static ItemModifierStat[] Stats() => Build().Stats;

    /// <summary>Every prefix and suffix row's weight on each base kind it rolls on.</summary>
    public static ItemModifierWeight[] Weights() => Build().Weights;

    /// <summary>The template links: each unique's own modifier and each base's implicit group's row.</summary>
    public static ItemTemplateModifier[] TemplateLinks() => Build().Links;

    /// <summary>The bases' drafted implicit stats by template id, in each base's line order.</summary>
    public static DraftedImplicit[] Implicits() => [.. DraftedImplicits().OrderBy(i => i.TemplateId)];

    private sealed record Family(
        string Code, ItemModifierKind Kind, bool Adds, (int Low, int High)[] Tiers, ItemBaseKind[] RollsOn,
        (DamageType? Element, string Name, StatType Stat)[] Groups);

    private sealed record Built(
        ItemModifierGroup[] Groups, ItemModifier[] Modifiers, ItemModifierStat[] Stats, ItemModifierWeight[] Weights,
        ItemTemplateModifier[] Links);

    private static Family Prefix(string code, ItemBaseKind[] rollsOn, (int, int)[] tiers, string name, StatType stat,
        bool adds = false) => new(code, ItemModifierKind.Prefix, adds, tiers, rollsOn, [(null, name, stat)]);

    private static Family Suffix(string code, ItemBaseKind[] rollsOn, (int, int)[] tiers, string name, StatType stat) =>
        new(code, ItemModifierKind.Suffix, false, tiers, rollsOn, [(null, name, stat)]);

    private static (int, int)[] Single(params int[] values) => [.. values.Select(value => (value, value))];

    private static Family[] Families() =>
    [
        Prefix("P1", s_weapons, [(1, 2), (2, 4), (3, 6), (5, 8), (6, 11)], "Briar-edged", StatType.AddedPhysicalDamage, adds: true),
        Prefix("P2", s_weapons, [(1, 3), (2, 5), (3, 7), (4, 10), (6, 13)], "Smouldering", StatType.AddedFireDamage, adds: true),
        Prefix("P3", s_weapons, [(1, 2), (2, 4), (3, 6), (4, 8), (5, 11)], "Rimed", StatType.AddedColdDamage, adds: true),
        Prefix("P4", s_weapons, [(1, 4), (1, 7), (1, 10), (2, 14), (2, 18)], "Crackling", StatType.AddedLightningDamage, adds: true),
        Prefix("P5", s_weapons, [(1, 2), (2, 4), (3, 6), (4, 8), (5, 11)], "Festering", StatType.AddedBlightDamage, adds: true),
        Prefix("P6", s_weapons, s_increasedLocal, "Honed", StatType.LocalIncreasedPhysicalDamage),
        Prefix("P7", [Mace, Staff, CasterOffHand], [(8, 12), (13, 20), (21, 30), (31, 40), (41, 50)], "Whispering", StatType.IncreasedSpellDamage),
        new("P8", ItemModifierKind.Prefix, false, [(5, 9), (10, 14), (15, 19), (20, 24), (25, 29)], [Staff, CasterOffHand, Amulet, Ring],
        [
            (DamageType.Fire, "Ashen", StatType.IncreasedFireDamage),
            (DamageType.Cold, "Hoarfrost", StatType.IncreasedColdDamage),
            (DamageType.Lightning, "Galvanic", StatType.IncreasedLightningDamage),
            (DamageType.Blight, "Mouldering", StatType.IncreasedBlightDamage),
        ]),
        new("P9", ItemModifierKind.Prefix, true, s_addedGlobal, [Ring, Amulet, .. s_gloves],
        [
            (DamageType.Physical, "Thorn-tipped", StatType.AddedPhysicalDamageToAttacks),
            (DamageType.Fire, "Thorn-tipped", StatType.AddedFireDamageToAttacks),
            (DamageType.Cold, "Thorn-tipped", StatType.AddedColdDamageToAttacks),
            (DamageType.Lightning, "Thorn-tipped", StatType.AddedLightningDamageToAttacks),
            (DamageType.Blight, "Thorn-tipped", StatType.AddedBlightDamageToAttacks),
        ]),
        new("P10", ItemModifierKind.Prefix, true, s_addedGlobal, [Ring, Amulet, CasterOffHand],
        [
            (DamageType.Fire, "Ember-sung", StatType.AddedFireDamageToSpells),
            (DamageType.Cold, "Ember-sung", StatType.AddedColdDamageToSpells),
            (DamageType.Lightning, "Ember-sung", StatType.AddedLightningDamageToSpells),
        ]),
        Prefix("P11", [.. s_armour, Shield, Ring, Amulet], [(5, 9), (10, 19), (20, 29), (30, 44), (45, 59)], "Hale", StatType.Health),
        Prefix("P12", [.. s_plate, Shield], [(2, 4), (4, 8), (8, 14), (14, 22), (22, 32)], "Barkbound", StatType.Armor),
        Prefix("P13", [.. s_plate, Shield], s_increasedLocal, "Ironbark", StatType.LocalIncreasedArmor),
        Prefix("P14", s_leather, [(10, 19), (20, 39), (40, 64), (65, 94), (95, 129)], "Fleet-footed", StatType.Evasion),
        Prefix("P15", s_leather, s_increasedLocal, "Mistwoven", StatType.LocalIncreasedEvasion),
        Prefix("P16", [.. s_cloth, CasterOffHand], [(3, 5), (6, 10), (11, 17), (18, 26), (27, 37)], "Veilwoven", StatType.Ward),
        Prefix("P17", [.. s_cloth, CasterOffHand], s_increasedLocal, "Glamoured", StatType.LocalIncreasedWard),
        Prefix("P18", [Amulet, Staff, CasterOffHand], s_overTime, "Lingering", StatType.IncreasedDamageOverTime),
        Prefix("P19", [Mace, Staff, CasterOffHand, Amulet], s_overTime, "Mending", StatType.IncreasedHealing),

        Suffix("S1", [.. s_armour, Sword, Mace, Bow, Shield, Ring, Amulet], s_attribute, "of the Ox-root", StatType.Strength),
        Suffix("S2", [.. s_armour, Sword, Staff, Bow, Ring, Amulet], s_attribute, "of the Hare", StatType.Agility),
        Suffix("S3", [.. s_armour, Sword, Mace, Staff, CasterOffHand, Ring, Amulet], s_attribute, "of the Owl", StatType.Intellect),
        Suffix("S4", [.. s_armour, Shield, Ring, Amulet], s_attribute, "of the Old Oak", StatType.Stamina),
        new("S5", ItemModifierKind.Suffix, false, [(6, 11), (12, 17), (18, 23), (24, 29), (30, 35)], [.. s_armour, Shield, Ring, Amulet],
        [
            (DamageType.Fire, "of the Hearth", StatType.FireResistance),
            (DamageType.Cold, "of the Thaw", StatType.ColdResistance),
            (DamageType.Lightning, "of Grounding", StatType.LightningResistance),
            (DamageType.Blight, "of the Clean Spring", StatType.BlightResistance),
        ]),
        Suffix("S6", s_weapons, [(5, 7), (8, 10), (11, 13), (14, 16), (17, 19)], "of Haste", StatType.LocalAttackSpeed),
        Suffix("S7", [.. s_gloves, Ring], [(3, 4), (5, 6), (7, 8), (9, 10)], "of Quick Hands", StatType.AttackSpeed),
        Suffix("S8", [Staff, Mace, Amulet, Ring, CasterOffHand], [(4, 6), (7, 9), (10, 12), (13, 15), (16, 18)], "of Cadence", StatType.CastSpeed),
        Suffix("S9", [.. s_weapons, CasterOffHand, Amulet, Ring], Single(1, 2, 3, 4, 5), "of the Keen Eye", StatType.CritPct),
        Suffix("S10", [.. s_weapons, Amulet], [(8, 12), (13, 17), (18, 22), (23, 27), (28, 32)], "of Ruin", StatType.CritMultiplier),
        Suffix("S11", s_boots, Single(5, 8, 11, 14, 17), "of the Wanderer", StatType.MovementSpeed),
        Suffix("S12", [.. s_helmets, .. s_shoulders, Amulet], [(3, 4), (5, 6), (7, 8), (9, 10), (11, 12)], "of Renewal", StatType.CooldownRecovery),
        Suffix("S13", [Shield], [(3, 4), (5, 6), (7, 8), (9, 10)], "of the Bulwark", StatType.BlockPct),
    ];

    // The uniques' drafted lines, by template: a stat and its value, and an "adds" line's high end.
    private static readonly (ulong Template, (StatType Stat, int Value, int? High)[] Lines)[] s_uniques =
    [
        (58, [(StatType.Health, 12, null), (StatType.Strength, 3, null), (StatType.Agility, 3, null), (StatType.IncreasedAttackDamage, 8, null)]),
        (64, [(StatType.Health, 20, null), (StatType.BlightResistance, 12, null), (StatType.IncreasedHealing, 8, null)]),
        (65, [(StatType.Health, 30, null), (StatType.AddedPhysicalDamageToAttacks, 2, 4), (StatType.CritPct, 4, null)]),
        (66, [(StatType.Health, 25, null), (StatType.IncreasedDamageOverTime, 15, null), (StatType.BlightResistance, 10, null)]),
        (67, [(StatType.Health, 20, null), (StatType.AttackSpeed, 6, null), (StatType.CastSpeed, 6, null)]),
    ];

    // The bases' drafted implicits: each armour set's defence by slot (head, chest, legs, hands, feet, shoulders), then the
    // shields' armour and block and the caster off-hands' ward and increased spell damage, by tier.
    private static IEnumerable<DraftedImplicit> DraftedImplicits() =>
    [
        .. Set(StatType.Armor, [36, 37, 38, 39, 40, 76], [5, 8, 6, 3, 3, 4]),
        .. Set(StatType.Armor, [12, 13, 14, 15, 16, 79], [11, 18, 14, 7, 7, 9]),
        .. Set(StatType.Armor, [85, 86, 87, 88, 89, 82], [19, 32, 26, 13, 13, 16]),
        .. Set(StatType.Evasion, [46, 47, 48, 49, 50, 77], [24, 40, 32, 16, 16, 20]),
        .. Set(StatType.Evasion, [22, 23, 24, 25, 26, 80], [54, 90, 72, 36, 36, 45]),
        .. Set(StatType.Evasion, [90, 91, 92, 93, 94, 83], [96, 160, 128, 64, 64, 80]),
        .. Set(StatType.Ward, [41, 42, 43, 44, 45, 78], [7, 12, 10, 5, 5, 6]),
        .. Set(StatType.Ward, [17, 18, 19, 20, 21, 81], [16, 26, 21, 10, 10, 13]),
        .. Set(StatType.Ward, [95, 96, 97, 98, 99, 84], [27, 45, 36, 18, 18, 23]),
        new(70, StatType.Armor, 6),
        new(70, StatType.BlockPct, 5),
        new(71, StatType.Armor, 14),
        new(71, StatType.BlockPct, 7),
        new(72, StatType.Armor, 24),
        new(72, StatType.BlockPct, 10),
        new(73, StatType.Ward, 8),
        new(73, StatType.IncreasedSpellDamage, 5),
        new(74, StatType.Ward, 18),
        new(74, StatType.IncreasedSpellDamage, 8),
        new(75, StatType.Ward, 30),
        new(75, StatType.IncreasedSpellDamage, 12),
    ];

    private static IEnumerable<DraftedImplicit> Set(StatType stat, ulong[] templates, int[] values) =>
        templates.Zip(values, (template, value) => new DraftedImplicit(template, stat, value));

    private static Built Build()
    {
        Dictionary<ulong, string> names = ItemBaseSeed.Equipment().ToDictionary(t => t.Id.Value, t => t.Name);
        List<ItemModifierGroup> groups = [];
        List<ItemModifier> modifiers = [];
        List<ItemModifierStat> stats = [];
        List<ItemModifierWeight> weights = [];
        List<ItemTemplateModifier> links = [];

        ItemModifierGroup NewGroup(ItemModifierKind kind, string? code, DamageType? element, string name)
        {
            var group = new ItemModifierGroup { Id = groups.Count + 1, Kind = kind, Code = code, Element = element, Name = name };
            groups.Add(group);
            return group;
        }

        int NewModifier(ItemModifierGroup group, byte? tier, ushort requiredItemLevel)
        {
            var modifier = new ItemModifier
            {
                Id = modifiers.Count + 1,
                GroupId = group.Id,
                Tier = tier,
                RequiredItemLevel = requiredItemLevel,
            };
            modifiers.Add(modifier);
            return modifier.Id;
        }

        foreach (Family family in Families())
        {
            foreach ((DamageType? element, string name, StatType stat) in family.Groups)
            {
                ItemModifierGroup group = NewGroup(family.Kind, family.Code, element, name);
                for (int tier = 0; tier < family.Tiers.Length; tier++)
                {
                    int id = NewModifier(group, (byte)(tier + 1), s_tierLevels[tier]);
                    (int low, int high) = family.Tiers[tier];
                    stats.Add(family.Adds
                        ? Line(id, 0, stat, (low, (low + high) / 2), ((low + high) / 2, high))
                        : Line(id, 0, stat, (low, high), null));
                    weights.AddRange(family.RollsOn.Order().Select(kind =>
                        new ItemModifierWeight { ModifierId = id, BaseKind = kind, Weight = s_tierWeights[tier] }));
                }
            }
        }

        foreach ((ulong template, (StatType Stat, int Value, int? High)[] lines) in s_uniques)
        {
            int id = NewModifier(NewGroup(ItemModifierKind.Unique, null, null, names[template]), null, 1);
            for (int index = 0; index < lines.Length; index++)
            {
                (StatType stat, int value, int? high) = lines[index];
                stats.Add(Line(id, index, stat, Spread(value, UniqueSpreadPercent),
                    high is { } h ? Spread(h, UniqueSpreadPercent) : null));
            }

            links.Add(new ItemTemplateModifier { TemplateId = template, Index = 0, ModifierId = id });
        }

        foreach (IGrouping<ulong, DraftedImplicit> drafted in Implicits().GroupBy(i => i.TemplateId))
        {
            int id = NewModifier(NewGroup(ItemModifierKind.Implicit, null, null, names[drafted.Key]), null, 1);
            int index = 0;
            foreach (DraftedImplicit line in drafted)
                stats.Add(Line(id, index++, line.Stat, Spread(line.Value, ImplicitSpreadPercent), null));

            links.Add(new ItemTemplateModifier { TemplateId = drafted.Key, Index = 0, ModifierId = id });
        }

        return new Built([.. groups], [.. modifiers], [.. stats], [.. weights], [.. links]);
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
