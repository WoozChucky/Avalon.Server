using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Database.World.Seeding;

/// <summary>
/// The seeded modifier catalog (#1179), built from the compact tables below: the affix families and their tiers, the
/// implicit groups, and the five uniques. The model's HasData, the catalog exports, the doc page and the tests read it
/// from here. A base's defence, block and weapon damage are not modifiers but its base stats (ItemBaseSeed); the
/// per-base implicits that held a base's defence (groups 50-109, rows 224-283) are retired, and their ids are never used
/// again.
/// <list type="bullet">
/// <item>Every element of a family split by element (P8, P9, P10, S5) is its own group; a unique is one group.</item>
/// <item>Every affix group carries one of the eight Resonance themes (#1180, <see cref="Themes" />), given beside its id
/// in the tables; a unique or an implicit group carries none.</item>
/// <item>An implicit group gives one stat, with rows T9, T8 and T7 whose ranges are the affix family's of that stat at
/// the same tier (P7's for increased projectile and physical damage, which no family gives; maximum power its own). It
/// has no weight: every base links the row of its own tier (ItemBaseSeed.Implicits), and the bases of one kind that
/// link one group are a line.</item>
/// <item>Ids are literal surrogates written in the tables: every group and every modifier row carries its own id, which
/// means nothing and never changes, since items store the modifier id. A row added later takes the next id above the
/// highest ever used, wherever it sits in the tables; a deleted id is never reused (so the rows of the retired fourth
/// and fifth tiers leave gaps). SeedIntegrityShould holds every existing row to its committed id.</item>
/// <item>Tiers count down: T9 is the weakest and T1 the best. Every family has T9, T8 and T7, the starting zone's,
/// unlocking at item level 1, 4 and 7 and weighing 1000, 800 and 600 on each base kind its group rolls on, and on no
/// other; T6 to T1 are left for later content. The values fit the starting zone's gear budget, and each tier beats the
/// one below at its best value. An "adds" line names its two ends exactly: "adds 1 to 3" seeds its low end 1-1 and its
/// high end 3-3, and each hit rolls between them.</item>
/// <item>A unique's lines are authored ranges sized to the starting zone's gear budget; it has no tier, no weight and no
/// item-level gate, and each copy rolls its named base's implicit before it.</item>
/// </list>
/// </summary>
public static class ItemModifierSeed
{
    // The Resonance themes' ids (#1180), literal surrogates, in the design's table order; ResonanceSeed names them too.
    internal const int Ember = 1;
    internal const int Rime = 2;
    internal const int Storm = 3;
    internal const int Wild = 4;
    internal const int Iron = 5;
    internal const int Thorn = 6;
    internal const int Gale = 7;
    internal const int Veil = 8;

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

    /// <summary>The Resonance themes (#1180), each with its literal id; every affix group names one.</summary>
    public static ItemTheme[] Themes() =>
    [
        new() { Id = Ember, Name = "Ember" },
        new() { Id = Rime, Name = "Rime" },
        new() { Id = Storm, Name = "Storm" },
        new() { Id = Wild, Name = "Wild" },
        new() { Id = Iron, Name = "Iron" },
        new() { Id = Thorn, Name = "Thorn" },
        new() { Id = Gale, Name = "Gale" },
        new() { Id = Veil, Name = "Veil" },
    ];

    /// <summary>The catalog groups, each with its literal id: affix families, implicits and uniques, by id.</summary>
    public static ItemModifierGroup[] Groups() => Build().Groups;

    /// <summary>The catalog rows, without navigations (HasData), each with its literal id.</summary>
    public static ItemModifier[] Modifiers() => Build().Modifiers;

    /// <summary>Every modifier's stat lines.</summary>
    public static ItemModifierStat[] Stats() => Build().Stats;

    /// <summary>Every prefix and suffix row's weight on each base kind it rolls on.</summary>
    public static ItemModifierWeight[] Weights() => Build().Weights;

    /// <summary>The template links: each base's implicit row at its tier, and each unique's own modifier.</summary>
    public static ItemTemplateModifier[] TemplateLinks() => Build().Links;

    /// <summary>
    /// One group of a family: its id, Resonance theme, element, name and stat, its rows' ids by tier (T9, T8, T7), and
    /// the base kinds it rolls on.
    /// </summary>
    private sealed record FamilyGroup(int Id, int Theme, DamageType? Element, string Name, StatType Stat, int[] Rows,
        ItemBaseKind[] RollsOn);

    private sealed record Family(string Code, ItemModifierKind Kind, bool Adds, (int Low, int High)[] Tiers, FamilyGroup[] Groups);

    private sealed record Built(
        ItemModifierGroup[] Groups, ItemModifier[] Modifiers, ItemModifierStat[] Stats, ItemModifierWeight[] Weights,
        ItemTemplateModifier[] Links);

    private static Family Prefix(string code, int group, int theme, int[] rows, ItemBaseKind[] rollsOn, (int, int)[] tiers,
        string name, StatType stat, bool adds = false) =>
        new(code, ItemModifierKind.Prefix, adds, tiers, [new(group, theme, null, name, stat, rows, rollsOn)]);

    private static Family Suffix(string code, int group, int theme, int[] rows, ItemBaseKind[] rollsOn, (int, int)[] tiers,
        string name, StatType stat) =>
        new(code, ItemModifierKind.Suffix, false, tiers, [new(group, theme, null, name, stat, rows, rollsOn)]);

    private static (int, int)[] Single(params int[] values) => [.. values.Select(value => (value, value))];

    private static readonly Family[] s_families =
    [
        Prefix("P1", 1, Thorn, [1, 2, 3], s_weapons, s_addsLocal, "Briar-edged", StatType.AddedPhysicalDamage, adds: true),
        Prefix("P2", 2, Ember, [6, 7, 8], s_weapons, s_addsLocal, "Smouldering", StatType.AddedFireDamage, adds: true),
        Prefix("P3", 3, Rime, [11, 12, 13], s_weapons, s_addsLocal, "Rimed", StatType.AddedColdDamage, adds: true),
        Prefix("P4", 4, Storm, [16, 17, 18], s_weapons, [(1, 2), (1, 3), (1, 4)], "Crackling", StatType.AddedLightningDamage, adds: true),
        Prefix("P5", 5, Wild, [21, 22, 23], s_weapons, s_addsLocal, "Festering", StatType.AddedBlightDamage, adds: true),
        Prefix("P6", 6, Thorn, [26, 27, 28], s_weapons, s_increasedLocal, "Honed", StatType.LocalIncreasedPhysicalDamage),
        Prefix("P7", 7, Veil, [31, 32, 33], [Mace, Staff, CasterOffHand], s_spellAndHealing, "Whispering", StatType.IncreasedSpellDamage),
        new("P8", ItemModifierKind.Prefix, false, s_smallPercent,
        [
            new(8, Ember, DamageType.Fire, "Ashen", StatType.IncreasedFireDamage, [36, 37, 38], s_increasedElement),
            new(9, Rime, DamageType.Cold, "Hoarfrost", StatType.IncreasedColdDamage, [41, 42, 43], s_increasedElement),
            new(10, Storm, DamageType.Lightning, "Galvanic", StatType.IncreasedLightningDamage, [46, 47, 48], s_increasedElement),
            new(11, Wild, DamageType.Blight, "Mouldering", StatType.IncreasedBlightDamage, [51, 52, 53], s_increasedElement),
        ]),
        new("P9", ItemModifierKind.Prefix, true, s_addsGlobal,
        [
            new(12, Thorn, DamageType.Physical, "Thorn-tipped", StatType.AddedPhysicalDamageToAttacks, [56, 57, 58], s_gloves),
            new(13, Ember, DamageType.Fire, "Thorn-tipped", StatType.AddedFireDamageToAttacks, [61, 62, 63], s_gloves),
            new(14, Rime, DamageType.Cold, "Thorn-tipped", StatType.AddedColdDamageToAttacks, [66, 67, 68], s_gloves),
            new(15, Storm, DamageType.Lightning, "Thorn-tipped", StatType.AddedLightningDamageToAttacks, [71, 72, 73], s_gloves),
            new(16, Wild, DamageType.Blight, "Thorn-tipped", StatType.AddedBlightDamageToAttacks, [76, 77, 78], s_gloves),
        ]),
        new("P10", ItemModifierKind.Prefix, true, s_addsGlobal,
        [
            new(17, Ember, DamageType.Fire, "Ember-sung", StatType.AddedFireDamageToSpells, [81, 82, 83], [Ring]),
            new(18, Rime, DamageType.Cold, "Ember-sung", StatType.AddedColdDamageToSpells, [86, 87, 88], [Ring]),
            new(19, Storm, DamageType.Lightning, "Ember-sung", StatType.AddedLightningDamageToSpells, [91, 92, 93], [Ring]),
        ]),
        Prefix("P11", 20, Wild, [96, 97, 98], [.. s_helmets, .. s_chests, .. s_legs, .. s_boots, Shield, Amulet], [(1, 2), (3, 4), (5, 5)], "Hale", StatType.Health),
        Prefix("P12", 21, Iron, [101, 102, 103], [.. s_plate, Shield], s_smallPercent, "Barkbound", StatType.Armor),
        Prefix("P13", 22, Iron, [106, 107, 108], [.. s_plate, Shield], s_increasedLocal, "Ironbark", StatType.LocalIncreasedArmor),
        Prefix("P14", 23, Gale, [111, 112, 113], s_leather, [(5, 8), (9, 14), (15, 20)], "Fleet-footed", StatType.Evasion),
        Prefix("P15", 24, Gale, [116, 117, 118], s_leather, s_increasedLocal, "Mistwoven", StatType.LocalIncreasedEvasion),
        Prefix("P16", 25, Veil, [121, 122, 123], [.. s_cloth, CasterOffHand], [(1, 2), (2, 4), (4, 6)], "Veilwoven", StatType.Ward),
        Prefix("P17", 26, Veil, [126, 127, 128], [.. s_cloth, CasterOffHand], s_increasedLocal, "Glamoured", StatType.LocalIncreasedWard),
        Prefix("P18", 27, Wild, [131, 132, 133], [Amulet, Staff, CasterOffHand], s_oneTwoThree, "Lingering", StatType.IncreasedDamageOverTime),
        Prefix("P19", 28, Wild, [136, 137, 138], [Mace, Staff, CasterOffHand, Amulet], s_spellAndHealing, "Mending", StatType.IncreasedHealing),

        Suffix("S1", 29, Thorn, [141, 142, 143], [.. s_chests, Ring], s_oneTwoThree, "of the Ox-root", StatType.Strength),
        Suffix("S2", 30, Gale, [146, 147, 148], [.. s_legs, Ring], s_oneTwoThree, "of the Hare", StatType.Agility),
        Suffix("S3", 31, Veil, [151, 152, 153], [.. s_shoulders, Ring], s_oneTwoThree, "of the Owl", StatType.Intellect),
        Suffix("S4", 32, Wild, [156, 157, 158], s_boots, s_oneTwoThree, "of the Old Oak", StatType.Stamina),
        new("S5", ItemModifierKind.Suffix, false, s_smallPercent,
        [
            new(33, Ember, DamageType.Fire, "of the Hearth", StatType.FireResistance, [161, 162, 163], [.. s_helmets, .. s_resistance]),
            new(34, Rime, DamageType.Cold, "of the Thaw", StatType.ColdResistance, [166, 167, 168], [.. s_boots, .. s_resistance]),
            new(35, Storm, DamageType.Lightning, "of Grounding", StatType.LightningResistance, [171, 172, 173], [.. s_legs, .. s_resistance]),
            new(36, Wild, DamageType.Blight, "of the Clean Spring", StatType.BlightResistance, [176, 177, 178], [.. s_chests, .. s_resistance]),
        ]),
        Suffix("S6", 37, Gale, [181, 182, 183], s_weapons, s_oneTwoThree, "of Haste", StatType.LocalAttackSpeed),
        Suffix("S7", 38, Gale, [186, 187, 188], s_gloves, s_oneTwoThree, "of Quick Hands", StatType.AttackSpeed),
        Suffix("S8", 39, Veil, [190, 191, 192], [Staff, CasterOffHand, .. s_gloves], s_oneTwoThree, "of Cadence", StatType.CastSpeed),
        Suffix("S9", 40, Thorn, [195, 196, 197], [.. s_weapons, .. s_gloves], s_oneTwoThree, "of the Keen Eye", StatType.CritPct),
        Suffix("S10", 41, Thorn, [200, 201, 202], [.. s_weapons, CasterOffHand, .. s_helmets, .. s_gloves], [(2, 3), (4, 5), (5, 6)], "of Ruin", StatType.CritMultiplier),
        Suffix("S11", 42, Gale, [205, 206, 207], s_boots, Single(2, 3, 4), "of the Wanderer", StatType.MovementSpeed),
        Suffix("S12", 43, Veil, [210, 211, 212], [.. s_helmets, .. s_shoulders], s_oneTwoThree, "of Renewal", StatType.CooldownRecovery),
        Suffix("S13", 44, Iron, [215, 216, 217], [Shield], s_smallPercent, "of the Bulwark", StatType.BlockPct),
        Suffix("S14", 110, Wild, [284, 285, 286], [.. s_shoulders, .. s_chests, .. s_legs], s_smallPercent, "of Deep Roots", StatType.IncreasedMaximumLife),
    ];

    /// <summary>
    /// One implicit group: its id, name (the stat's words) and stat, its rows' ids by tier (T9, T8, T7), and where its
    /// ranges come from: the affix family giving the same stat, the family <paramref name="Source" /> names (P7's tiers
    /// for the two increased-damage lines no family gives), or its own <paramref name="Own" /> ranges.
    /// </summary>
    private sealed record ImplicitGroup(int Id, string Name, StatType Stat, int[] Rows, string? Source = null,
        (int Low, int High)[]? Own = null);

    // The implicit groups, one per stat a base's implicit gives (the content table's themes), shared by every base that
    // gives it: each base links the row of its own tier (ItemBaseSeed.Implicits).
    private static readonly ImplicitGroup[] s_implicits =
    [
        new(111, "Fire Resistance", StatType.FireResistance, [287, 288, 289]),
        new(112, "Cold Resistance", StatType.ColdResistance, [290, 291, 292]),
        new(113, "Lightning Resistance", StatType.LightningResistance, [293, 294, 295]),
        new(114, "Blight Resistance", StatType.BlightResistance, [296, 297, 298]),
        new(115, "Increased Armor", StatType.LocalIncreasedArmor, [299, 300, 301]),
        new(116, "Increased Health", StatType.IncreasedMaximumLife, [302, 303, 304]),
        new(117, "Physical Damage to Attacks", StatType.AddedPhysicalDamageToAttacks, [305, 306, 307]),
        new(118, "Strength", StatType.Strength, [308, 309, 310]),
        new(119, "Stamina", StatType.Stamina, [311, 312, 313]),
        new(120, "Crit Multiplier", StatType.CritMultiplier, [314, 315, 316]),
        new(121, "Agility", StatType.Agility, [317, 318, 319]),
        new(122, "Increased Evasion", StatType.LocalIncreasedEvasion, [320, 321, 322]),
        new(123, "Increased Projectile Damage", StatType.IncreasedProjectileDamage, [323, 324, 325], Source: "P7"),
        new(124, "Movement Speed", StatType.MovementSpeed, [326, 327, 328]),
        new(125, "Cooldown Recovery", StatType.CooldownRecovery, [329, 330, 331]),
        new(126, "Intellect", StatType.Intellect, [332, 333, 334]),
        new(127, "Increased Spell Damage", StatType.IncreasedSpellDamage, [335, 336, 337]),
        new(128, "Cast Speed", StatType.CastSpeed, [338, 339, 340]),
        new(129, "Power", StatType.Power, [341, 342, 343], Own: [(10, 15), (20, 25), (30, 40)]),
        new(130, "Health", StatType.Health, [344, 345, 346]),
        new(131, "Increased Physical Damage", StatType.IncreasedPhysicalDamage, [347, 348, 349], Source: "P7"),
        new(132, "Lightning Damage to Attacks", StatType.AddedLightningDamageToAttacks, [350, 351, 352]),
        new(133, "Fire Damage to Spells", StatType.AddedFireDamageToSpells, [353, 354, 355]),
        new(134, "Cold Damage to Spells", StatType.AddedColdDamageToSpells, [356, 357, 358]),
        new(135, "Increased Damage Over Time", StatType.IncreasedDamageOverTime, [359, 360, 361]),
        new(136, "Increased Healing", StatType.IncreasedHealing, [362, 363, 364]),
    ];

    /// <summary>A unique's line: its range, and an "adds" line's high end (<paramref name="Min2" /> to <paramref name="Max2" />).</summary>
    private sealed record UniqueLine(StatType Stat, int Min, int Max, int? Min2 = null, int? Max2 = null);

    // The uniques: group id, modifier id, template, and their own lines as authored (the content table's Uniques column,
    // sized to the starting zone's gear budget). Each copy rolls its named base's implicit before them.
    private static readonly (int Group, int Row, ulong Template, UniqueLine[] Lines)[] s_uniques =
    [
        (45, 219, 58, [new(StatType.Health, 4, 5), new(StatType.Strength, 2, 3), new(StatType.Agility, 2, 3), new(StatType.IncreasedAttackDamage, 6, 10)]),
        (46, 220, 64, [new(StatType.Health, 4, 5), new(StatType.BlightResistance, 5, 6), new(StatType.IncreasedHealing, 5, 7)]),
        (47, 221, 65, [new(StatType.Health, 4, 5), new(StatType.AddedPhysicalDamageToAttacks, 1, 1, 3, 3), new(StatType.CritMultiplier, 3, 4)]),
        (48, 222, 66, [new(StatType.IncreasedMaximumLife, 4, 5), new(StatType.IncreasedDamageOverTime, 2, 3)]),
        (49, 223, 67, [new(StatType.IncreasedMaximumLife, 4, 5), new(StatType.IncreasedAttackDamage, 5, 7), new(StatType.IncreasedMeleeDamage, 5, 7)]),
    ];

    private static Built Build()
    {
        Dictionary<ulong, string> names = ItemBaseSeed.Equipment().ToDictionary(t => t.Id.Value, t => t.Name);
        List<ItemModifierGroup> groups = [];
        List<ItemModifier> modifiers = [];
        List<ItemModifierStat> stats = [];
        List<ItemModifierWeight> weights = [];
        List<ItemTemplateModifier> links = [];

        void AddGroup(int id, ItemModifierKind kind, string? code, DamageType? element, int? theme, string name) =>
            groups.Add(new ItemModifierGroup { Id = id, Kind = kind, Code = code, Element = element, ThemeId = theme, Name = name });

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

                AddGroup(group.Id, family.Kind, family.Code, group.Element, group.Theme, group.Name);
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

        foreach (ImplicitGroup group in s_implicits)
        {
            (bool adds, (int Low, int High)[] tiers) = group.Own is { } own
                ? (false, own)
                : (SourceOf(group).Adds, SourceOf(group).Tiers);
            if (group.Rows.Length != s_tiers.Length || tiers.Length != s_tiers.Length)
                throw new InvalidOperationException($"Implicit {group.Stat} needs a row id and a range for each of the {s_tiers.Length} tiers.");

            AddGroup(group.Id, ItemModifierKind.Implicit, null, null, null, group.Name);
            for (int tier = 0; tier < s_tiers.Length; tier++)
            {
                int id = group.Rows[tier];
                AddModifier(id, group.Id, s_tiers[tier], s_tierLevels[tier]);
                (int low, int high) = tiers[tier];
                stats.Add(adds
                    ? Line(id, 0, group.Stat, (low, low), (high, high))
                    : Line(id, 0, group.Stat, (low, high), null));
            }
        }

        foreach ((int group, int row, ulong template, UniqueLine[] lines) in s_uniques)
        {
            AddGroup(group, ItemModifierKind.Unique, null, null, null, names[template]);
            AddModifier(row, group, null, 1);
            for (int index = 0; index < lines.Length; index++)
            {
                UniqueLine line = lines[index];
                stats.Add(Line(row, index, line.Stat, (line.Min, line.Max),
                    line.Min2 is { } min2 && line.Max2 is { } max2 ? (min2, max2) : null));
            }

            links.Add(new ItemTemplateModifier { TemplateId = template, Index = 0, ModifierId = row });
        }

        // Every base links the row of its implicit group at its own tier.
        Dictionary<ulong, ItemTemplate> templates = ItemBaseSeed.Equipment().ToDictionary(t => t.Id.Value);
        foreach (BaseImplicit implicitLine in ItemBaseSeed.Implicits())
        {
            ImplicitGroup group = s_implicits.Single(g => g.Stat == implicitLine.Stat);
            byte tier = ItemBaseSeed.ImplicitTierOf(templates[implicitLine.TemplateId].RequiredLevel);
            int row = group.Rows[Array.IndexOf(s_tiers, tier)];
            links.Add(new ItemTemplateModifier { TemplateId = implicitLine.TemplateId, Index = 0, ModifierId = row });
        }

        return new Built(
            [.. groups.OrderBy(g => g.Id)], [.. modifiers.OrderBy(m => m.Id)], [.. stats], [.. weights],
            [.. links.OrderBy(l => l.TemplateId.Value)]);
    }

    /// <summary>
    /// The code of the affix family an implicit group's ranges come from (#1179): the one it names, or the one giving the
    /// same stat; null for a group with its own ranges (Power). What the doc page's Implicits table shows.
    /// </summary>
    public static string? ImplicitSource(int groupId) =>
        s_implicits.Single(g => g.Id == groupId) is var group && group.Own is null ? SourceOf(group).Code : null;

    // The family whose tiers an implicit group takes.
    private static Family SourceOf(ImplicitGroup group) => group.Source is { } code
        ? s_families.Single(family => family.Code == code)
        : s_families.Single(family => family.Groups.Any(g => g.Stat == group.Stat));

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
