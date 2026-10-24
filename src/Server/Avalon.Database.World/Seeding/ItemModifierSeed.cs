using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Database.World.Seeding;

/// <summary>
/// The seeded modifier catalog (#1179), built from the compact tables below: the affix families and their tiers, and the
/// five uniques. The model's HasData, the catalog exports, the doc page and the tests read it from here. A base's defence,
/// block and weapon damage are not modifiers but its base stats (ItemBaseSeed); the per-base implicits that held a
/// base's defence (groups 50-109, rows 224-283) are retired, and their ids are never used again.
/// <list type="bullet">
/// <item>Every element of a family split by element (P8, P9, P10, S5) is its own group; a unique is one group.</item>
/// <item>Ids are literal surrogates written in the tables: every group and every modifier row carries its own id, which
/// means nothing and never changes, since items store the modifier id. A row added later takes the next id above the
/// highest ever used, wherever it sits in the tables; a deleted id is never reused (so the rows of the retired fourth
/// and fifth tiers leave gaps). SeedIntegrityShould holds every existing row to its committed id.</item>
/// <item>Tiers count down: T9 is the weakest and T1 the best. Every family has T9, T8 and T7, the starting zone's,
/// unlocking at item level 1, 4 and 7 and weighing 1000, 800 and 600 on each base kind its group rolls on, and on no
/// other; T6 to T1 are left for later content. The values fit the starting zone's gear budget, and each tier beats the
/// one below at its best value. An "adds" line names its two ends exactly: "adds 1 to 3" seeds its low end 1-1 and its
/// high end 3-3, and each hit rolls between them.</item>
/// <item>A unique's lines are its drafted values ±20 %, rounded half away from zero and at least 1; it has no tier, no
/// weight and no item-level gate.</item>
/// </list>
/// </summary>
public static class ItemModifierSeed
{
    private const int UniqueSpreadPercent = 20;

    // The tiers every family has, weakest first, with their item levels and weights.
    private static readonly byte[] s_tiers = [9, 8, 7];
    private static readonly ushort[] s_tierLevels = [1, 4, 7];
    private static readonly int[] s_tierWeights = [1000, 800, 600];

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

    private static readonly ItemBaseKind[] s_helmets = [ItemBaseKind.PlateHead, ItemBaseKind.LeatherHead, ItemBaseKind.ClothHead];
    private static readonly ItemBaseKind[] s_shoulders = [ItemBaseKind.PlateShoulders, ItemBaseKind.LeatherShoulders, ItemBaseKind.ClothShoulders];
    private static readonly ItemBaseKind[] s_chests = [ItemBaseKind.PlateChest, ItemBaseKind.LeatherChest, ItemBaseKind.ClothChest];
    private static readonly ItemBaseKind[] s_gloves = [ItemBaseKind.PlateHands, ItemBaseKind.LeatherHands, ItemBaseKind.ClothHands];
    private static readonly ItemBaseKind[] s_legs = [ItemBaseKind.PlateLegs, ItemBaseKind.LeatherLegs, ItemBaseKind.ClothLegs];
    private static readonly ItemBaseKind[] s_boots = [ItemBaseKind.PlateFeet, ItemBaseKind.LeatherFeet, ItemBaseKind.ClothFeet];
    private static readonly ItemBaseKind[] s_weapons = [ItemBaseKind.Sword, ItemBaseKind.Mace, ItemBaseKind.Staff, ItemBaseKind.Bow];

    private const ItemBaseKind Mace = ItemBaseKind.Mace;
    private const ItemBaseKind Staff = ItemBaseKind.Staff;
    private const ItemBaseKind Shield = ItemBaseKind.Shield;
    private const ItemBaseKind CasterOffHand = ItemBaseKind.CasterOffHand;
    private const ItemBaseKind Amulet = ItemBaseKind.Amulet;
    private const ItemBaseKind Ring = ItemBaseKind.Ring;

    // Where a split family's elements roll: P8's increased elemental damage everywhere alike; a resistance on the shield,
    // the caster off-hand and the amulet, and on one armour slot of its own.
    private static readonly ItemBaseKind[] s_increasedElement = [Staff, CasterOffHand, Amulet, Ring, .. s_shoulders];
    private static readonly ItemBaseKind[] s_resistance = [Shield, CasterOffHand, Amulet];

    // The tier ranges (T9, T8, T7) shared by several families. An "adds" family's pair is its low end and its high end.
    private static readonly (int, int)[] s_increasedLocal = [(5, 8), (9, 12), (13, 16)];
    private static readonly (int, int)[] s_oneTwoThree = Single(1, 2, 3);
    private static readonly (int, int)[] s_smallPercent = [(1, 2), (2, 3), (3, 4)];
    private static readonly (int, int)[] s_spellAndHealing = [(2, 3), (3, 4), (5, 6)];
    private static readonly (int, int)[] s_addsLocal = [(1, 1), (1, 2), (2, 3)];
    private static readonly (int, int)[] s_addsGlobal = [(1, 1), (1, 2), (1, 3)];

    /// <summary>The catalog groups, each with its literal id: affix families, then uniques.</summary>
    public static ItemModifierGroup[] Groups() => Build().Groups;

    /// <summary>The catalog rows, without navigations (HasData), each with its literal id.</summary>
    public static ItemModifier[] Modifiers() => Build().Modifiers;

    /// <summary>Every modifier's stat lines.</summary>
    public static ItemModifierStat[] Stats() => Build().Stats;

    /// <summary>Every prefix and suffix row's weight on each base kind it rolls on.</summary>
    public static ItemModifierWeight[] Weights() => Build().Weights;

    /// <summary>The template links: each unique's own modifier.</summary>
    public static ItemTemplateModifier[] TemplateLinks() => Build().Links;

    /// <summary>
    /// One group of a family: its id, element, name and stat, its rows' ids by tier (T9, T8, T7), and the base kinds it
    /// rolls on.
    /// </summary>
    private sealed record FamilyGroup(int Id, DamageType? Element, string Name, StatType Stat, int[] Rows, ItemBaseKind[] RollsOn);

    private sealed record Family(string Code, ItemModifierKind Kind, bool Adds, (int Low, int High)[] Tiers, FamilyGroup[] Groups);

    private sealed record Built(
        ItemModifierGroup[] Groups, ItemModifier[] Modifiers, ItemModifierStat[] Stats, ItemModifierWeight[] Weights,
        ItemTemplateModifier[] Links);

    private static Family Prefix(string code, int group, int[] rows, ItemBaseKind[] rollsOn, (int, int)[] tiers, string name,
        StatType stat, bool adds = false) =>
        new(code, ItemModifierKind.Prefix, adds, tiers, [new(group, null, name, stat, rows, rollsOn)]);

    private static Family Suffix(string code, int group, int[] rows, ItemBaseKind[] rollsOn, (int, int)[] tiers, string name,
        StatType stat) =>
        new(code, ItemModifierKind.Suffix, false, tiers, [new(group, null, name, stat, rows, rollsOn)]);

    private static (int, int)[] Single(params int[] values) => [.. values.Select(value => (value, value))];

    private static readonly Family[] s_families =
    [
        Prefix("P1", 1, [1, 2, 3], s_weapons, s_addsLocal, "Briar-edged", StatType.AddedPhysicalDamage, adds: true),
        Prefix("P2", 2, [6, 7, 8], s_weapons, s_addsLocal, "Smouldering", StatType.AddedFireDamage, adds: true),
        Prefix("P3", 3, [11, 12, 13], s_weapons, s_addsLocal, "Rimed", StatType.AddedColdDamage, adds: true),
        Prefix("P4", 4, [16, 17, 18], s_weapons, [(1, 2), (1, 3), (1, 4)], "Crackling", StatType.AddedLightningDamage, adds: true),
        Prefix("P5", 5, [21, 22, 23], s_weapons, s_addsLocal, "Festering", StatType.AddedBlightDamage, adds: true),
        Prefix("P6", 6, [26, 27, 28], s_weapons, s_increasedLocal, "Honed", StatType.LocalIncreasedPhysicalDamage),
        Prefix("P7", 7, [31, 32, 33], [Mace, Staff, CasterOffHand], s_spellAndHealing, "Whispering", StatType.IncreasedSpellDamage),
        new("P8", ItemModifierKind.Prefix, false, s_smallPercent,
        [
            new(8, DamageType.Fire, "Ashen", StatType.IncreasedFireDamage, [36, 37, 38], s_increasedElement),
            new(9, DamageType.Cold, "Hoarfrost", StatType.IncreasedColdDamage, [41, 42, 43], s_increasedElement),
            new(10, DamageType.Lightning, "Galvanic", StatType.IncreasedLightningDamage, [46, 47, 48], s_increasedElement),
            new(11, DamageType.Blight, "Mouldering", StatType.IncreasedBlightDamage, [51, 52, 53], s_increasedElement),
        ]),
        new("P9", ItemModifierKind.Prefix, true, s_addsGlobal,
        [
            new(12, DamageType.Physical, "Thorn-tipped", StatType.AddedPhysicalDamageToAttacks, [56, 57, 58], s_gloves),
            new(13, DamageType.Fire, "Thorn-tipped", StatType.AddedFireDamageToAttacks, [61, 62, 63], s_gloves),
            new(14, DamageType.Cold, "Thorn-tipped", StatType.AddedColdDamageToAttacks, [66, 67, 68], s_gloves),
            new(15, DamageType.Lightning, "Thorn-tipped", StatType.AddedLightningDamageToAttacks, [71, 72, 73], s_gloves),
            new(16, DamageType.Blight, "Thorn-tipped", StatType.AddedBlightDamageToAttacks, [76, 77, 78], s_gloves),
        ]),
        new("P10", ItemModifierKind.Prefix, true, s_addsGlobal,
        [
            new(17, DamageType.Fire, "Ember-sung", StatType.AddedFireDamageToSpells, [81, 82, 83], [Ring]),
            new(18, DamageType.Cold, "Ember-sung", StatType.AddedColdDamageToSpells, [86, 87, 88], [Ring]),
            new(19, DamageType.Lightning, "Ember-sung", StatType.AddedLightningDamageToSpells, [91, 92, 93], [Ring]),
        ]),
        Prefix("P11", 20, [96, 97, 98], [.. s_helmets, .. s_chests, .. s_legs, .. s_boots, Shield, Amulet], [(1, 2), (3, 4), (5, 5)], "Hale", StatType.Health),
        Prefix("P12", 21, [101, 102, 103], [.. s_plate, Shield], s_smallPercent, "Barkbound", StatType.Armor),
        Prefix("P13", 22, [106, 107, 108], [.. s_plate, Shield], s_increasedLocal, "Ironbark", StatType.LocalIncreasedArmor),
        Prefix("P14", 23, [111, 112, 113], s_leather, [(5, 8), (9, 14), (15, 20)], "Fleet-footed", StatType.Evasion),
        Prefix("P15", 24, [116, 117, 118], s_leather, s_increasedLocal, "Mistwoven", StatType.LocalIncreasedEvasion),
        Prefix("P16", 25, [121, 122, 123], [.. s_cloth, CasterOffHand], [(1, 2), (2, 4), (4, 6)], "Veilwoven", StatType.Ward),
        Prefix("P17", 26, [126, 127, 128], [.. s_cloth, CasterOffHand], s_increasedLocal, "Glamoured", StatType.LocalIncreasedWard),
        Prefix("P18", 27, [131, 132, 133], [Amulet, Staff, CasterOffHand], s_oneTwoThree, "Lingering", StatType.IncreasedDamageOverTime),
        Prefix("P19", 28, [136, 137, 138], [Mace, Staff, CasterOffHand, Amulet], s_spellAndHealing, "Mending", StatType.IncreasedHealing),

        Suffix("S1", 29, [141, 142, 143], [.. s_chests, Ring], s_oneTwoThree, "of the Ox-root", StatType.Strength),
        Suffix("S2", 30, [146, 147, 148], [.. s_legs, Ring], s_oneTwoThree, "of the Hare", StatType.Agility),
        Suffix("S3", 31, [151, 152, 153], [.. s_shoulders, Ring], s_oneTwoThree, "of the Owl", StatType.Intellect),
        Suffix("S4", 32, [156, 157, 158], s_boots, s_oneTwoThree, "of the Old Oak", StatType.Stamina),
        new("S5", ItemModifierKind.Suffix, false, s_smallPercent,
        [
            new(33, DamageType.Fire, "of the Hearth", StatType.FireResistance, [161, 162, 163], [.. s_helmets, .. s_resistance]),
            new(34, DamageType.Cold, "of the Thaw", StatType.ColdResistance, [166, 167, 168], [.. s_boots, .. s_resistance]),
            new(35, DamageType.Lightning, "of Grounding", StatType.LightningResistance, [171, 172, 173], [.. s_legs, .. s_resistance]),
            new(36, DamageType.Blight, "of the Clean Spring", StatType.BlightResistance, [176, 177, 178], [.. s_chests, .. s_resistance]),
        ]),
        Suffix("S6", 37, [181, 182, 183], s_weapons, s_oneTwoThree, "of Haste", StatType.LocalAttackSpeed),
        Suffix("S7", 38, [186, 187, 188], s_gloves, s_oneTwoThree, "of Quick Hands", StatType.AttackSpeed),
        Suffix("S8", 39, [190, 191, 192], [Staff, CasterOffHand, .. s_gloves], s_oneTwoThree, "of Cadence", StatType.CastSpeed),
        Suffix("S9", 40, [195, 196, 197], [.. s_weapons, .. s_gloves], s_oneTwoThree, "of the Keen Eye", StatType.CritPct),
        Suffix("S10", 41, [200, 201, 202], [.. s_weapons, CasterOffHand, .. s_helmets, .. s_gloves], [(2, 3), (4, 5), (5, 6)], "of Ruin", StatType.CritMultiplier),
        Suffix("S11", 42, [205, 206, 207], s_boots, Single(2, 3, 4), "of the Wanderer", StatType.MovementSpeed),
        Suffix("S12", 43, [210, 211, 212], [.. s_helmets, .. s_shoulders], s_oneTwoThree, "of Renewal", StatType.CooldownRecovery),
        Suffix("S13", 44, [215, 216, 217], [Shield], s_smallPercent, "of the Bulwark", StatType.BlockPct),
        Suffix("S14", 110, [284, 285, 286], [.. s_shoulders, .. s_chests, .. s_legs], s_smallPercent, "of Deep Roots", StatType.IncreasedMaximumLife),
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
                if (group.Rows.Length != s_tiers.Length || family.Tiers.Length != s_tiers.Length)
                {
                    throw new InvalidOperationException(
                        $"{family.Code} {group.Element} needs a row id and a range for each of the {s_tiers.Length} tiers.");
                }

                AddGroup(group.Id, family.Kind, family.Code, group.Element, group.Name);
                for (int tier = 0; tier < s_tiers.Length; tier++)
                {
                    int id = group.Rows[tier];
                    AddModifier(id, group.Id, s_tiers[tier], s_tierLevels[tier]);
                    (int low, int high) = family.Tiers[tier];
                    stats.Add(family.Adds
                        ? Line(id, 0, group.Stat, (low, low), (high, high))
                        : Line(id, 0, group.Stat, (low, high), null));
                    weights.AddRange(group.RollsOn.Order().Select(kind =>
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
                stats.Add(Line(row, index, stat, ItemBaseSeed.Spread(value, UniqueSpreadPercent),
                    high is { } h ? ItemBaseSeed.Spread(h, UniqueSpreadPercent) : null));
            }

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
}
