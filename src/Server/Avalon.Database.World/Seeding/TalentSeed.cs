using System.Globalization;
using System.Text.RegularExpressions;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using static Avalon.Domain.World.StatType;

namespace Avalon.Database.World.Seeding;

/// <summary>
/// The seeded talent tree (#1181): the first region of the wheel every class shares, without its capstones and keystones
/// (Part D adds them, from id 306). The model's HasData, the catalog's fallback, the export and the tests read it from
/// here.
/// <list type="bullet">
/// <item>Ids are literal surrogates written in the table, in the design's table order: the Warrior's region 1-73, the
/// Hunter's 74-146, the Wizard's 147-219, the Cleric's 220-292 and the shared nodes 293-305. An id means nothing and never
/// changes, since characters store the ids they hold; a node added later takes the next id above the highest ever used.</item>
/// <item>Each row carries the design's label (<c>WH-A5</c>: the Warrior's half-path toward the Hunter, lane A, node 5;
/// <c>.c1</c> a node of the branch hanging off it; <c>Whub-L2</c> the Warrior's spoke, left route, node 2). A label is a
/// key of this source only, never stored, exported or sent: it wires the edges, written below as chains of labels, places
/// the node on the wheel (<see cref="Layout" />), and tells the reader which node a row is.</item>
/// <item>A start links its class's starter ability and an ability node the ability it unlocks; every other node carries
/// stat lines, all <see cref="TalentStatKind.Added" />, in the gear's vocabulary.</item>
/// </list>
/// </summary>
public static partial class TalentSeed
{
    // The abilities the tree links, by their literal ids.
    private const uint Cleave = 200;
    private const uint GroundSlam = 201;
    private const uint HurledAxe = 202;
    private const uint Rend = 203;
    private const uint ShieldBash = 204;
    private const uint SparkBolt = 210;
    private const uint FlameBurst = 211;
    private const uint FrostFan = 212;
    private const uint Ignite = 213;
    private const uint FrostBind = 214;
    private const uint QuickShot = 220;
    private const uint PiercingArrow = 221;
    private const uint ScatterShot = 222;
    private const uint CripplingShot = 223;
    private const uint BindingShot = 224;
    private const uint Smite = 230;
    private const uint RadiantPulse = 231;
    private const uint MendingCircle = 232;
    private const uint Renew = 233;
    private const uint Fortify = 234;
    private const uint Hush = 235;
    private const uint HeavyStrike = 319;
    private const uint Frostbolt = 320;

    /// <summary>The whole tree, as the catalog, the export and the API read it.</summary>
    public static TalentTreeRows Rows() => Build();

    /// <summary>Every node, by id, without its links.</summary>
    public static TalentNode[] Nodes() => [.. Build().Nodes];

    /// <summary>Every edge, once, the smaller id first, by (NodeA, NodeB).</summary>
    public static TalentEdge[] Edges() => [.. Build().Edges];

    /// <summary>Every node's stat lines.</summary>
    public static TalentNodeStat[] Stats() => [.. Build().Stats];

    /// <summary>Each start's and each ability node's ability.</summary>
    public static TalentNodeAbility[] Abilities() => [.. Build().Abilities];

    /// <summary>The capstone forks: none until Part D seeds the capstones.</summary>
    public static TalentForkGroup[] ForkGroups() => [.. Build().ForkGroups];

    /// <summary>The node each class starts on.</summary>
    public static TalentClassStart[] Starts() => [.. Build().Starts];

    /// <summary>One node: its id, label, kind, name, the class starting on it, the ability it links, and its stat lines.</summary>
    private sealed record Row(int Id, string Label, TalentNodeKind Kind, string? Name, CharacterClass? Class, uint? Ability,
        Line[] Lines);

    /// <summary>A stat line; an "adds X to Y" line has its high end in <paramref name="ValueMax" />.</summary>
    private readonly record struct Line(StatType Stat, int Value, int? ValueMax = null);

    private static Row Start(int id, string label, CharacterClass @class, string name, uint ability) =>
        new(id, label, TalentNodeKind.Start, name, @class, ability, []);

    private static Row Small(int id, string label, params Line[] lines) =>
        new(id, label, TalentNodeKind.Small, null, null, null, lines);

    private static Row Notable(int id, string label, string name, params Line[] lines) =>
        new(id, label, TalentNodeKind.Notable, name, null, null, lines);

    private static Row Ability(int id, string label, string name, uint ability) =>
        new(id, label, TalentNodeKind.Ability, name, null, ability, []);

    private static Row Junction(int id, string label, params Line[] lines) =>
        new(id, label, TalentNodeKind.Junction, null, null, null, lines);

    private static Line L(StatType stat, int value) => new(stat, value);

    private static Line Adds(StatType stat, int low, int high) => new(stat, low, high);

    private static string[] Chain(params string[] labels) => labels;

    private static readonly Row[] s_rows =
    [
        // Warrior region (Heavy Strike)
        Start(1, "W-start", CharacterClass.Warrior, "Warrior start", HeavyStrike),
        Small(2, "WH-A1", L(Strength, 5)),
        Small(3, "WH-A2", L(IncreasedPhysicalDamage, 5)),
        Small(4, "WH-A3", L(AttackSpeed, 2)),
        Small(5, "WH-A4", L(IncreasedMaximumLife, 3)),
        Ability(6, "WH-A5", "Cleave", Cleave),
        Small(7, "WH-A6", L(IncreasedMeleeDamage, 5)),
        Small(8, "WH-A7", L(Strength, 5)),
        Notable(9, "WH-A8", "Cleaving Force", L(IncreasedAreaDamage, 15)),
        Small(10, "WH-A9", L(AttackSpeed, 2)),
        Small(11, "WH-A10", L(IncreasedAttackDamage, 5)),
        Small(12, "WH-B1", L(Strength, 5)),
        Small(13, "WH-B2", L(IncreasedMaximumLife, 3)),
        Small(14, "WH-B3", L(IncreasedPhysicalDamage, 5)),
        Notable(15, "WH-B4", "Bloodletter", L(IncreasedDamageOverTime, 15)),
        Small(16, "WH-B5", L(IncreasedDamageOverTime, 5)),
        Small(17, "WH-B6", L(AttackSpeed, 2)),
        Small(18, "WH-B7", L(IncreasedPhysicalDamage, 5)),
        Small(19, "WH-B8", L(Agility, 5)),
        Small(20, "WH-B9", L(Strength, 5)),
        Ability(21, "WH-B10", "Rend", Rend),
        Small(22, "WH-A5.c1", L(IncreasedAreaDamage, 5)),
        Small(23, "WH-A5.c2", L(IncreasedMeleeDamage, 5)),
        Small(24, "WH-A5.c3", L(AttackSpeed, 2)),
        Small(25, "WH-A5.c4", L(IncreasedAreaDamage, 5)),
        Small(26, "WH-A5.c5", L(IncreasedMeleeDamage, 5)),
        Small(27, "WH-B10.c1", L(IncreasedDamageOverTime, 5)),
        Small(28, "WH-B10.c2", L(IncreasedPhysicalDamage, 5)),
        Small(29, "WH-B10.c3", L(Strength, 5)),
        Small(30, "WH-B10.c4", L(IncreasedDamageOverTime, 5)),
        Small(31, "WH-B10.c5", L(IncreasedPhysicalDamage, 5)),
        Small(32, "WC-A1", L(Armor, 15)),
        Small(33, "WC-A2", L(Stamina, 5)),
        Small(34, "WC-A3", L(IncreasedArmour, 6)),
        Small(35, "WC-A4", L(Strength, 5)),
        Ability(36, "WC-A5", "Shield Bash", ShieldBash),
        Small(37, "WC-A6", L(BlockPct, 1)),
        Small(38, "WC-A7", L(IncreasedMaximumLife, 3)),
        Notable(39, "WC-A8", "Bulwark", L(BlockPct, 2), L(IncreasedArmour, 6)),
        Small(40, "WC-A9", L(IncreasedArmour, 6)),
        Small(41, "WC-A10", L(LightningResistance, 4)),
        Small(42, "WC-B1", L(Stamina, 5)),
        Small(43, "WC-B2", L(IncreasedArmour, 6)),
        Small(44, "WC-B3", L(Strength, 5)),
        Notable(45, "WC-B4", "Iron Skin", L(IncreasedArmour, 18)),
        Small(46, "WC-B5", L(IncreasedMaximumLife, 3)),
        Small(47, "WC-B6", L(IncreasedAreaDamage, 5)),
        Small(48, "WC-B7", L(IncreasedMeleeDamage, 5)),
        Small(49, "WC-B8", L(IncreasedWard, 6)),
        Small(50, "WC-B9", L(Armor, 15)),
        Ability(51, "WC-B10", "Ground Slam", GroundSlam),
        Small(52, "WC-B10.c1", L(IncreasedAreaDamage, 5)),
        Small(53, "WC-B10.c2", L(IncreasedMeleeDamage, 5)),
        Small(54, "WC-B10.c3", L(IncreasedArmour, 6)),
        Small(55, "WC-B10.c4", L(IncreasedAreaDamage, 5)),
        Small(56, "WC-B10.c5", L(IncreasedMeleeDamage, 5)),
        Small(57, "Whub-S1", L(Strength, 5)),
        Small(58, "Whub-S2", L(IncreasedMaximumLife, 3)),
        Small(59, "Whub-S3", L(IncreasedPhysicalDamage, 5)),
        Small(60, "Whub-L1", L(IncreasedProjectileDamage, 5)),
        Ability(61, "Whub-L2", "Hurled Axe", HurledAxe),
        Small(62, "Whub-L3", L(AttackSpeed, 2)),
        Small(63, "Whub-R1", L(Armor, 15)),
        Small(64, "Whub-R2", L(IncreasedMaximumLife, 3)),
        Small(65, "Whub-R3", L(BlockPct, 1)),
        Small(66, "Whub-S4", L(Strength, 5)),
        Small(67, "Whub-S5", L(IncreasedAttackDamage, 5)),
        Notable(68, "Whub-S6", "Brawn", L(Strength, 15)),
        Small(69, "Whub-L2.c1", L(IncreasedProjectileDamage, 5)),
        Small(70, "Whub-L2.c2", L(AttackSpeed, 2)),
        Small(71, "Whub-L2.c3", L(Strength, 5)),
        Small(72, "Whub-L2.c4", L(IncreasedProjectileDamage, 5)),
        Small(73, "Whub-L2.c5", L(AttackSpeed, 2)),

        // Hunter region (Quick Shot)
        Start(74, "H-start", CharacterClass.Hunter, "Hunter start", QuickShot),
        Small(75, "HW-A1", L(Agility, 5)),
        Small(76, "HW-A2", L(IncreasedProjectileDamage, 5)),
        Small(77, "HW-A3", L(AttackSpeed, 2)),
        Small(78, "HW-A4", L(IncreasedMaximumLife, 3)),
        Ability(79, "HW-A5", "Scatter Shot", ScatterShot),
        Small(80, "HW-A6", L(IncreasedAreaDamage, 5)),
        Small(81, "HW-A7", L(Agility, 5)),
        Notable(82, "HW-A8", "Eagle Eye", L(CritPct, 3)),
        Small(83, "HW-A9", L(IncreasedAttackDamage, 5)),
        Small(84, "HW-A10", L(Strength, 5)),
        Small(85, "HW-B1", L(Agility, 5)),
        Small(86, "HW-B2", L(Evasion, 20)),
        Small(87, "HW-B3", L(IncreasedProjectileDamage, 5)),
        Notable(88, "HW-B4", "Fleetfoot", L(IncreasedEvasion, 18)),
        Small(89, "HW-B5", L(CritPct, 1)),
        Small(90, "HW-B6", L(AttackSpeed, 2)),
        Small(91, "HW-B7", L(IncreasedPhysicalDamage, 5)),
        Small(92, "HW-B8", L(CritMultiplier, 5)),
        Small(93, "HW-B9", L(Agility, 5)),
        Ability(94, "HW-B10", "Piercing Arrow", PiercingArrow),
        Small(95, "HW-A5.c1", L(IncreasedAreaDamage, 5)),
        Small(96, "HW-A5.c2", L(IncreasedAttackDamage, 5)),
        Small(97, "HW-A5.c3", L(AttackSpeed, 2)),
        Small(98, "HW-A5.c4", L(IncreasedAreaDamage, 5)),
        Small(99, "HW-A5.c5", L(IncreasedAttackDamage, 5)),
        Small(100, "HW-B10.c1", L(IncreasedProjectileDamage, 5)),
        Small(101, "HW-B10.c2", L(AttackSpeed, 2)),
        Small(102, "HW-B10.c3", L(CritPct, 1)),
        Small(103, "HW-B10.c4", L(IncreasedProjectileDamage, 5)),
        Small(104, "HW-B10.c5", L(AttackSpeed, 2)),
        Small(105, "HZ-A1", L(Agility, 5)),
        Small(106, "HZ-A2", L(IncreasedColdDamage, 5)),
        Small(107, "HZ-A3", L(AttackSpeed, 2)),
        Small(108, "HZ-A4", L(IncreasedProjectileDamage, 5)),
        Ability(109, "HZ-A5", "Crippling Shot", CripplingShot),
        Small(110, "HZ-A6", L(IncreasedColdDamage, 5)),
        Small(111, "HZ-A7", L(MovementSpeed, 2)),
        Notable(112, "HZ-A8", "Frostfletched", Adds(AddedColdDamageToAttacks, 2, 4)),
        Small(113, "HZ-A9", L(IncreasedProjectileDamage, 5)),
        Small(114, "HZ-A10", L(Intellect, 5)),
        Small(115, "HZ-B1", L(Agility, 5)),
        Small(116, "HZ-B2", L(IncreasedProjectileDamage, 5)),
        Small(117, "HZ-B3", L(IncreasedBlightDamage, 5)),
        Notable(118, "HZ-B4", "Venom Tips", L(IncreasedBlightDamage, 15)),
        Small(119, "HZ-B5", L(AttackSpeed, 2)),
        Small(120, "HZ-B6", L(IncreasedBlightDamage, 5)),
        Small(121, "HZ-B7", L(Evasion, 20)),
        Small(122, "HZ-B8", L(CastSpeed, 2)),
        Small(123, "HZ-B9", L(Agility, 5)),
        Ability(124, "HZ-B10", "Binding Shot", BindingShot),
        Small(125, "HZ-A5.c1", L(IncreasedColdDamage, 5)),
        Small(126, "HZ-A5.c2", L(IncreasedProjectileDamage, 5)),
        Small(127, "HZ-A5.c3", L(AttackSpeed, 2)),
        Small(128, "HZ-A5.c4", L(IncreasedColdDamage, 5)),
        Small(129, "HZ-A5.c5", L(IncreasedProjectileDamage, 5)),
        Small(130, "HZ-B10.c1", L(IncreasedBlightDamage, 5)),
        Small(131, "HZ-B10.c2", L(IncreasedProjectileDamage, 5)),
        Small(132, "HZ-B10.c3", L(Agility, 5)),
        Small(133, "HZ-B10.c4", L(IncreasedBlightDamage, 5)),
        Small(134, "HZ-B10.c5", L(IncreasedProjectileDamage, 5)),
        Small(135, "Hhub-S1", L(Agility, 5)),
        Small(136, "Hhub-S2", L(Evasion, 20)),
        Small(137, "Hhub-S3", L(CritPct, 1)),
        Small(138, "Hhub-L1", L(CritMultiplier, 5)),
        Notable(139, "Hhub-L2", "Marksman", L(CritMultiplier, 15)),
        Small(140, "Hhub-L3", L(IncreasedProjectileDamage, 5)),
        Small(141, "Hhub-R1", L(IncreasedMaximumLife, 3)),
        Small(142, "Hhub-R2", L(Evasion, 20)),
        Small(143, "Hhub-R3", L(MovementSpeed, 2)),
        Small(144, "Hhub-S4", L(Agility, 5)),
        Small(145, "Hhub-S5", L(AttackSpeed, 2)),
        Notable(146, "Hhub-S6", "Nimble", L(Agility, 15)),

        // Wizard region (Spark Bolt)
        Start(147, "Z-start", CharacterClass.Wizard, "Wizard start", SparkBolt),
        Small(148, "ZH-A1", L(Intellect, 5)),
        Small(149, "ZH-A2", L(IncreasedColdDamage, 5)),
        Small(150, "ZH-A3", L(IncreasedSpellDamage, 5)),
        Small(151, "ZH-A4", L(IncreasedMaximumLife, 3)),
        Ability(152, "ZH-A5", "Frost Fan", FrostFan),
        Small(153, "ZH-A6", L(IncreasedAreaDamage, 5)),
        Small(154, "ZH-A7", L(Intellect, 5)),
        Notable(155, "ZH-A8", "Bitter Cold", L(IncreasedColdDamage, 15)),
        Small(156, "ZH-A9", L(CastSpeed, 2)),
        Small(157, "ZH-A10", L(Agility, 5)),
        Small(158, "ZH-B1", L(Intellect, 5)),
        Small(159, "ZH-B2", L(IncreasedSpellDamage, 5)),
        Small(160, "ZH-B3", L(IncreasedColdDamage, 5)),
        Notable(161, "ZH-B4", "Deep Reserves", L(IncreasedMaximumMana, 15)),
        Small(162, "ZH-B5", L(CastSpeed, 2)),
        Small(163, "ZH-B6", L(IncreasedColdDamage, 5)),
        Small(164, "ZH-B7", L(IncreasedAreaDamage, 5)),
        Small(165, "ZH-B8", L(IncreasedProjectileDamage, 5)),
        Small(166, "ZH-B9", L(Intellect, 5)),
        Ability(167, "ZH-B10", "Frost Bind", FrostBind),
        Small(168, "ZH-A5.c1", L(IncreasedColdDamage, 5)),
        Small(169, "ZH-A5.c2", L(IncreasedAreaDamage, 5)),
        Small(170, "ZH-A5.c3", L(CastSpeed, 2)),
        Small(171, "ZH-A5.c4", L(IncreasedColdDamage, 5)),
        Small(172, "ZH-A5.c5", L(IncreasedAreaDamage, 5)),
        Small(173, "ZH-B10.c1", L(IncreasedAreaDamage, 5)),
        Small(174, "ZH-B10.c2", L(IncreasedColdDamage, 5)),
        Small(175, "ZH-B10.c3", L(Intellect, 5)),
        Small(176, "ZH-B10.c4", L(IncreasedAreaDamage, 5)),
        Small(177, "ZH-B10.c5", L(IncreasedColdDamage, 5)),
        Small(178, "ZC-A1", L(Intellect, 5)),
        Small(179, "ZC-A2", L(IncreasedFireDamage, 5)),
        Small(180, "ZC-A3", L(IncreasedSpellDamage, 5)),
        Small(181, "ZC-A4", L(Intellect, 5)),
        Ability(182, "ZC-A5", "Flame Burst", FlameBurst),
        Small(183, "ZC-A6", L(IncreasedFireDamage, 5)),
        Small(184, "ZC-A7", L(CastSpeed, 2)),
        Notable(185, "ZC-A8", "Kindling", L(IncreasedFireDamage, 15)),
        Small(186, "ZC-A9", L(IncreasedSpellDamage, 5)),
        Small(187, "ZC-A10", L(IncreasedLightningDamage, 5)),
        Small(188, "ZC-B1", L(Intellect, 5)),
        Small(189, "ZC-B2", L(Power, 10)),
        Small(190, "ZC-B3", L(IncreasedFireDamage, 5)),
        Notable(191, "ZC-B4", "Arcane Flow", L(IncreasedManaRegeneration, 25)),
        Small(192, "ZC-B5", L(IncreasedDamageOverTime, 5)),
        Small(193, "ZC-B6", L(IncreasedFireDamage, 5)),
        Small(194, "ZC-B7", L(IncreasedDamageOverTime, 5)),
        Small(195, "ZC-B8", L(CastSpeed, 2)),
        Small(196, "ZC-B9", L(IncreasedManaRegeneration, 8)),
        Ability(197, "ZC-B10", "Ignite", Ignite),
        Small(198, "ZC-A5.c1", L(IncreasedFireDamage, 5)),
        Small(199, "ZC-A5.c2", L(IncreasedAreaDamage, 5)),
        Small(200, "ZC-A5.c3", L(CastSpeed, 2)),
        Small(201, "ZC-A5.c4", L(IncreasedFireDamage, 5)),
        Small(202, "ZC-A5.c5", L(IncreasedAreaDamage, 5)),
        Small(203, "ZC-B10.c1", L(IncreasedDamageOverTime, 5)),
        Small(204, "ZC-B10.c2", L(IncreasedFireDamage, 5)),
        Small(205, "ZC-B10.c3", L(Intellect, 5)),
        Small(206, "ZC-B10.c4", L(IncreasedDamageOverTime, 5)),
        Small(207, "ZC-B10.c5", L(IncreasedFireDamage, 5)),
        Small(208, "Zhub-S1", L(Intellect, 5)),
        Small(209, "Zhub-S2", L(IncreasedManaRegeneration, 8)),
        Small(210, "Zhub-S3", L(IncreasedSpellDamage, 5)),
        Small(211, "Zhub-L1", L(CastSpeed, 2)),
        Notable(212, "Zhub-L2", "Quickened Casting", L(CastSpeed, 6)),
        Small(213, "Zhub-L3", L(IncreasedSpellDamage, 5)),
        Small(214, "Zhub-R1", L(Ward, 8)),
        Small(215, "Zhub-R2", L(IncreasedWard, 6)),
        Small(216, "Zhub-R3", L(IncreasedMaximumLife, 3)),
        Small(217, "Zhub-S4", L(Intellect, 5)),
        Small(218, "Zhub-S5", L(IncreasedMaximumMana, 5)),
        Notable(219, "Zhub-S6", "Studious", L(Intellect, 15)),

        // Cleric region (Smite)
        Start(220, "C-start", CharacterClass.Cleric, "Cleric start", Smite),
        Small(221, "CZ-A1", L(Intellect, 5)),
        Small(222, "CZ-A2", L(IncreasedLightningDamage, 5)),
        Small(223, "CZ-A3", L(IncreasedSpellDamage, 5)),
        Small(224, "CZ-A4", L(IncreasedMaximumLife, 3)),
        Ability(225, "CZ-A5", "Radiant Pulse", RadiantPulse),
        Small(226, "CZ-A6", L(IncreasedAreaDamage, 5)),
        Small(227, "CZ-A7", L(Intellect, 5)),
        Notable(228, "CZ-A8", "Storm Hymn", L(IncreasedLightningDamage, 15)),
        Small(229, "CZ-A9", L(CastSpeed, 2)),
        Small(230, "CZ-A10", L(IncreasedSpellDamage, 5)),
        Small(231, "CZ-B1", L(Intellect, 5)),
        Small(232, "CZ-B2", L(IncreasedSpellDamage, 5)),
        Small(233, "CZ-B3", L(IncreasedLightningDamage, 5)),
        Notable(234, "CZ-B4", "Zeal", L(CastSpeed, 6)),
        Small(235, "CZ-B5", L(CooldownRecovery, 2)),
        Small(236, "CZ-B6", L(IncreasedLightningDamage, 5)),
        Small(237, "CZ-B7", L(IncreasedProjectileDamage, 5)),
        Small(238, "CZ-B8", L(IncreasedFireDamage, 5)),
        Small(239, "CZ-B9", L(Intellect, 5)),
        Ability(240, "CZ-B10", "Hush", Hush),
        Small(241, "CZ-A5.c1", L(IncreasedLightningDamage, 5)),
        Small(242, "CZ-A5.c2", L(IncreasedAreaDamage, 5)),
        Small(243, "CZ-A5.c3", L(CastSpeed, 2)),
        Small(244, "CZ-A5.c4", L(IncreasedLightningDamage, 5)),
        Small(245, "CZ-A5.c5", L(IncreasedAreaDamage, 5)),
        Small(246, "CW-A1", L(IncreasedHealing, 5)),
        Small(247, "CW-A2", L(Intellect, 5)),
        Small(248, "CW-A3", L(IncreasedWard, 6)),
        Small(249, "CW-A4", L(Stamina, 5)),
        Ability(250, "CW-A5", "Mending Circle", MendingCircle),
        Small(251, "CW-A6", L(IncreasedHealing, 5)),
        Small(252, "CW-A7", L(IncreasedWard, 6)),
        Notable(253, "CW-A8", "Ward of Faith", L(IncreasedWard, 18)),
        Small(254, "CW-A9", L(IncreasedHealing, 5)),
        Small(255, "CW-A10", L(IncreasedArmour, 6)),
        Small(256, "CW-B1", L(Stamina, 5)),
        Small(257, "CW-B2", L(IncreasedHealing, 5)),
        Small(258, "CW-B3", L(Ward, 8)),
        Notable(259, "CW-B4", "Devotion", L(IncreasedHealing, 15)),
        Small(260, "CW-B5", L(IncreasedMaximumLife, 3)),
        Small(261, "CW-B6", L(IncreasedHealing, 5)),
        Small(262, "CW-B7", L(BlockPct, 1)),
        Small(263, "CW-B8", L(Strength, 5)),
        Small(264, "CW-B9", L(IncreasedHealing, 5)),
        Ability(265, "CW-B10", "Renew", Renew),
        Small(266, "CW-A5.c1", L(IncreasedHealing, 5)),
        Small(267, "CW-A5.c2", L(CooldownRecovery, 2)),
        Small(268, "CW-A5.c3", L(Intellect, 5)),
        Small(269, "CW-A5.c4", L(IncreasedHealing, 5)),
        Small(270, "CW-A5.c5", L(CooldownRecovery, 2)),
        Small(271, "CW-B10.c1", L(IncreasedHealing, 5)),
        Small(272, "CW-B10.c2", L(IncreasedHealing, 5)),
        Small(273, "CW-B10.c3", L(IncreasedMaximumLife, 3)),
        Small(274, "CW-B10.c4", L(IncreasedHealing, 5)),
        Small(275, "CW-B10.c5", L(IncreasedHealing, 5)),
        Small(276, "Chub-S1", L(Intellect, 5)),
        Small(277, "Chub-S2", L(IncreasedHealing, 5)),
        Small(278, "Chub-S3", L(IncreasedWard, 6)),
        Small(279, "Chub-L1", L(Armor, 15)),
        Ability(280, "Chub-L2", "Fortify", Fortify),
        Small(281, "Chub-L3", L(IncreasedMaximumLife, 3)),
        Small(282, "Chub-R1", L(BlightResistance, 4)),
        Small(283, "Chub-R2", L(IncreasedManaRegeneration, 8)),
        Small(284, "Chub-R3", L(IncreasedHealing, 5)),
        Small(285, "Chub-S4", L(Intellect, 5)),
        Small(286, "Chub-S5", L(CooldownRecovery, 2)),
        Notable(287, "Chub-S6", "Faithful", L(Intellect, 15)),
        Small(288, "Chub-L2.c1", L(IncreasedArmour, 6)),
        Small(289, "Chub-L2.c2", L(CooldownRecovery, 2)),
        Small(290, "Chub-L2.c3", L(IncreasedMaximumLife, 3)),
        Small(291, "Chub-L2.c4", L(IncreasedArmour, 6)),
        Small(292, "Chub-L2.c5", L(CooldownRecovery, 2)),

        // Shared nodes: the joints, Frostbolt's branch and the hub junctions
        Notable(293, "WH-joint", "Weapon Mastery", L(IncreasedAttackDamage, 10), L(AttackSpeed, 3)),
        Ability(294, "HZ-joint", "Frostbolt", Frostbolt),
        Notable(295, "ZC-joint", "Spellweaver", L(IncreasedSpellDamage, 10), L(CastSpeed, 3)),
        Notable(296, "CW-joint", "Holy Bulwark", L(IncreasedArmour, 10), L(IncreasedWard, 10)),
        Small(297, "HZ-joint.c1", L(IncreasedColdDamage, 5)),
        Small(298, "HZ-joint.c2", L(IncreasedProjectileDamage, 5)),
        Small(299, "HZ-joint.c3", L(CastSpeed, 2)),
        Small(300, "HZ-joint.c4", L(IncreasedColdDamage, 5)),
        Small(301, "HZ-joint.c5", L(IncreasedProjectileDamage, 5)),
        Junction(302, "hub-W", L(IncreasedMaximumLife, 3)),
        Junction(303, "hub-H", L(IncreasedMaximumLife, 3)),
        Junction(304, "hub-Z", L(IncreasedMaximumLife, 3)),
        Junction(305, "hub-C", L(IncreasedMaximumLife, 3)),
    ];

    // The edges, as chains of labels: each lane from its start to its joint, the lanes' crossings at nodes 3, 6 and 9,
    // each branch off its ability, each spoke's two routes to its hub junction, and the hub ring.
    private static readonly string[][] s_chains =
    [
        // Warrior region
        Chain("W-start", "WH-A1", "WH-A2", "WH-A3", "WH-A4", "WH-A5", "WH-A6", "WH-A7", "WH-A8", "WH-A9", "WH-A10", "WH-joint"),
        Chain("W-start", "WH-B1", "WH-B2", "WH-B3", "WH-B4", "WH-B5", "WH-B6", "WH-B7", "WH-B8", "WH-B9", "WH-B10", "WH-joint"),
        Chain("WH-A3", "WH-B3"), Chain("WH-A6", "WH-B6"), Chain("WH-A9", "WH-B9"),
        Chain("WH-A5", "WH-A5.c1", "WH-A5.c2", "WH-A5.c3", "WH-A5.c4", "WH-A5.c5"),
        Chain("WH-B10", "WH-B10.c1", "WH-B10.c2", "WH-B10.c3", "WH-B10.c4", "WH-B10.c5"),
        Chain("W-start", "WC-A1", "WC-A2", "WC-A3", "WC-A4", "WC-A5", "WC-A6", "WC-A7", "WC-A8", "WC-A9", "WC-A10", "CW-joint"),
        Chain("W-start", "WC-B1", "WC-B2", "WC-B3", "WC-B4", "WC-B5", "WC-B6", "WC-B7", "WC-B8", "WC-B9", "WC-B10", "CW-joint"),
        Chain("WC-A3", "WC-B3"), Chain("WC-A6", "WC-B6"), Chain("WC-A9", "WC-B9"),
        Chain("WC-B10", "WC-B10.c1", "WC-B10.c2", "WC-B10.c3", "WC-B10.c4", "WC-B10.c5"),
        Chain("W-start", "Whub-S1", "Whub-S2", "Whub-S3", "Whub-L1", "Whub-L2", "Whub-L3", "Whub-S4", "Whub-S5", "Whub-S6", "hub-W"),
        Chain("Whub-S3", "Whub-R1", "Whub-R2", "Whub-R3", "Whub-S4"),
        Chain("Whub-L2", "Whub-L2.c1", "Whub-L2.c2", "Whub-L2.c3", "Whub-L2.c4", "Whub-L2.c5"),

        // Hunter region
        Chain("H-start", "HW-A1", "HW-A2", "HW-A3", "HW-A4", "HW-A5", "HW-A6", "HW-A7", "HW-A8", "HW-A9", "HW-A10", "WH-joint"),
        Chain("H-start", "HW-B1", "HW-B2", "HW-B3", "HW-B4", "HW-B5", "HW-B6", "HW-B7", "HW-B8", "HW-B9", "HW-B10", "WH-joint"),
        Chain("HW-A3", "HW-B3"), Chain("HW-A6", "HW-B6"), Chain("HW-A9", "HW-B9"),
        Chain("HW-A5", "HW-A5.c1", "HW-A5.c2", "HW-A5.c3", "HW-A5.c4", "HW-A5.c5"),
        Chain("HW-B10", "HW-B10.c1", "HW-B10.c2", "HW-B10.c3", "HW-B10.c4", "HW-B10.c5"),
        Chain("H-start", "HZ-A1", "HZ-A2", "HZ-A3", "HZ-A4", "HZ-A5", "HZ-A6", "HZ-A7", "HZ-A8", "HZ-A9", "HZ-A10", "HZ-joint"),
        Chain("H-start", "HZ-B1", "HZ-B2", "HZ-B3", "HZ-B4", "HZ-B5", "HZ-B6", "HZ-B7", "HZ-B8", "HZ-B9", "HZ-B10", "HZ-joint"),
        Chain("HZ-A3", "HZ-B3"), Chain("HZ-A6", "HZ-B6"), Chain("HZ-A9", "HZ-B9"),
        Chain("HZ-A5", "HZ-A5.c1", "HZ-A5.c2", "HZ-A5.c3", "HZ-A5.c4", "HZ-A5.c5"),
        Chain("HZ-B10", "HZ-B10.c1", "HZ-B10.c2", "HZ-B10.c3", "HZ-B10.c4", "HZ-B10.c5"),
        Chain("H-start", "Hhub-S1", "Hhub-S2", "Hhub-S3", "Hhub-L1", "Hhub-L2", "Hhub-L3", "Hhub-S4", "Hhub-S5", "Hhub-S6", "hub-H"),
        Chain("Hhub-S3", "Hhub-R1", "Hhub-R2", "Hhub-R3", "Hhub-S4"),

        // Wizard region
        Chain("Z-start", "ZH-A1", "ZH-A2", "ZH-A3", "ZH-A4", "ZH-A5", "ZH-A6", "ZH-A7", "ZH-A8", "ZH-A9", "ZH-A10", "HZ-joint"),
        Chain("Z-start", "ZH-B1", "ZH-B2", "ZH-B3", "ZH-B4", "ZH-B5", "ZH-B6", "ZH-B7", "ZH-B8", "ZH-B9", "ZH-B10", "HZ-joint"),
        Chain("ZH-A3", "ZH-B3"), Chain("ZH-A6", "ZH-B6"), Chain("ZH-A9", "ZH-B9"),
        Chain("ZH-A5", "ZH-A5.c1", "ZH-A5.c2", "ZH-A5.c3", "ZH-A5.c4", "ZH-A5.c5"),
        Chain("ZH-B10", "ZH-B10.c1", "ZH-B10.c2", "ZH-B10.c3", "ZH-B10.c4", "ZH-B10.c5"),
        Chain("Z-start", "ZC-A1", "ZC-A2", "ZC-A3", "ZC-A4", "ZC-A5", "ZC-A6", "ZC-A7", "ZC-A8", "ZC-A9", "ZC-A10", "ZC-joint"),
        Chain("Z-start", "ZC-B1", "ZC-B2", "ZC-B3", "ZC-B4", "ZC-B5", "ZC-B6", "ZC-B7", "ZC-B8", "ZC-B9", "ZC-B10", "ZC-joint"),
        Chain("ZC-A3", "ZC-B3"), Chain("ZC-A6", "ZC-B6"), Chain("ZC-A9", "ZC-B9"),
        Chain("ZC-A5", "ZC-A5.c1", "ZC-A5.c2", "ZC-A5.c3", "ZC-A5.c4", "ZC-A5.c5"),
        Chain("ZC-B10", "ZC-B10.c1", "ZC-B10.c2", "ZC-B10.c3", "ZC-B10.c4", "ZC-B10.c5"),
        Chain("Z-start", "Zhub-S1", "Zhub-S2", "Zhub-S3", "Zhub-L1", "Zhub-L2", "Zhub-L3", "Zhub-S4", "Zhub-S5", "Zhub-S6", "hub-Z"),
        Chain("Zhub-S3", "Zhub-R1", "Zhub-R2", "Zhub-R3", "Zhub-S4"),

        // Cleric region
        Chain("C-start", "CZ-A1", "CZ-A2", "CZ-A3", "CZ-A4", "CZ-A5", "CZ-A6", "CZ-A7", "CZ-A8", "CZ-A9", "CZ-A10", "ZC-joint"),
        Chain("C-start", "CZ-B1", "CZ-B2", "CZ-B3", "CZ-B4", "CZ-B5", "CZ-B6", "CZ-B7", "CZ-B8", "CZ-B9", "CZ-B10", "ZC-joint"),
        Chain("CZ-A3", "CZ-B3"), Chain("CZ-A6", "CZ-B6"), Chain("CZ-A9", "CZ-B9"),
        Chain("CZ-A5", "CZ-A5.c1", "CZ-A5.c2", "CZ-A5.c3", "CZ-A5.c4", "CZ-A5.c5"),
        Chain("C-start", "CW-A1", "CW-A2", "CW-A3", "CW-A4", "CW-A5", "CW-A6", "CW-A7", "CW-A8", "CW-A9", "CW-A10", "CW-joint"),
        Chain("C-start", "CW-B1", "CW-B2", "CW-B3", "CW-B4", "CW-B5", "CW-B6", "CW-B7", "CW-B8", "CW-B9", "CW-B10", "CW-joint"),
        Chain("CW-A3", "CW-B3"), Chain("CW-A6", "CW-B6"), Chain("CW-A9", "CW-B9"),
        Chain("CW-A5", "CW-A5.c1", "CW-A5.c2", "CW-A5.c3", "CW-A5.c4", "CW-A5.c5"),
        Chain("CW-B10", "CW-B10.c1", "CW-B10.c2", "CW-B10.c3", "CW-B10.c4", "CW-B10.c5"),
        Chain("C-start", "Chub-S1", "Chub-S2", "Chub-S3", "Chub-L1", "Chub-L2", "Chub-L3", "Chub-S4", "Chub-S5", "Chub-S6", "hub-C"),
        Chain("Chub-S3", "Chub-R1", "Chub-R2", "Chub-R3", "Chub-S4"),
        Chain("Chub-L2", "Chub-L2.c1", "Chub-L2.c2", "Chub-L2.c3", "Chub-L2.c4", "Chub-L2.c5"),

        // Shared: Frostbolt's branch and the hub ring
        Chain("HZ-joint", "HZ-joint.c1", "HZ-joint.c2", "HZ-joint.c3", "HZ-joint.c4", "HZ-joint.c5"),
        Chain("hub-W", "hub-H", "hub-Z", "hub-C", "hub-W"),
    ];

    private static TalentTreeRows Build()
    {
        var ids = new Dictionary<string, int>(s_rows.Length, StringComparer.Ordinal);
        List<TalentNode> nodes = [];
        List<TalentNodeStat> stats = [];
        List<TalentNodeAbility> abilities = [];
        List<TalentClassStart> starts = [];
        foreach (Row row in s_rows)
        {
            ids.Add(row.Label, row.Id);
            (float angle, float radius) = Layout(row.Label);
            nodes.Add(new TalentNode { Id = row.Id, Kind = row.Kind, Name = row.Name, Angle = angle, Radius = radius });
            foreach (Line line in row.Lines)
            {
                stats.Add(new TalentNodeStat
                {
                    NodeId = row.Id,
                    Stat = line.Stat,
                    Kind = TalentStatKind.Added,
                    Value = line.Value,
                    ValueMax = line.ValueMax,
                });
            }

            if (row.Ability is { } ability)
                abilities.Add(new TalentNodeAbility { NodeId = row.Id, AbilityId = new AbilityId(ability) });
            if (row.Class is { } @class)
                starts.Add(new TalentClassStart { Class = @class, NodeId = row.Id });
        }

        var edges = new SortedSet<(int A, int B)>();
        foreach (string[] chain in s_chains)
        {
            for (int i = 1; i < chain.Length; i++)
            {
                int a = ids[chain[i - 1]];
                int b = ids[chain[i]];
                if (!edges.Add((Math.Min(a, b), Math.Max(a, b))))
                    throw new InvalidOperationException($"The talent edge {chain[i - 1]} - {chain[i]} is written twice.");
            }
        }

        return new TalentTreeRows(nodes, [.. edges.Select(e => new TalentEdge { NodeA = e.A, NodeB = e.B })], stats,
            abilities, [], starts);
    }

    /// <summary>
    /// Where a node sits on the wheel, worked out from its label (the design leaves the layout to the builder). Angles in
    /// degrees, in (-180, 180]: the Warrior's start at -90, the Hunter's at 0, the Wizard's at 90 and the Cleric's at 180,
    /// each at radius 600, in layout units.
    /// <list type="bullet">
    /// <item>A lane's node n sits 4n degrees from its start toward the neighbour it leads to, lane A outside the ring (640)
    /// and lane B inside it (560); a branch steps 40 units a node away from the ring at its ability's angle, outward off
    /// lane A and inward off lane B.</item>
    /// <item>A joint sits 45 degrees round from the start its label names first, at 600, its branch stepping outward.</item>
    /// <item>A spoke runs inward along its start's angle, 40 units a node, its left and right routes 4 degrees either side;
    /// a spoke branch steps 4 degrees a node further left at radius 400; the hub junctions sit at 160.</item>
    /// </list>
    /// </summary>
    private static (float Angle, float Radius) Layout(string label)
    {
        Match m = LabelPattern().Match(label);
        if (!m.Success)
            throw new InvalidOperationException($"The talent label '{label}' has no place on the wheel.");

        if (m.Groups["hub"].Success)
            return (Theta(m.Groups["hub"].ValueSpan[0]), 160f);

        char x = m.Groups["x"].ValueSpan[0];
        float theta = Theta(x);
        int n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].ValueSpan, CultureInfo.InvariantCulture) : 0;
        int k = m.Groups["k"].Success ? int.Parse(m.Groups["k"].ValueSpan, CultureInfo.InvariantCulture) : 0;

        if (m.Groups["route"].Success)
        {
            if (k > 0)
                return (Normalize(theta - 4 - (4 * k)), 400f);
            return m.Groups["route"].ValueSpan[0] switch
            {
                'S' => (theta, n <= 3 ? 600 - (40 * n) : 360 - (40 * (n - 3))),
                'L' => (Normalize(theta - 4), 480 - (40 * n)),
                _ => (Normalize(theta + 4), 480 - (40 * n)),
            };
        }

        if (!m.Groups["y"].Success)
            return (theta, 600f);
        if (!m.Groups["lane"].Success)
            return (Normalize(theta + 45), 600 + (40 * k));

        int toward = Next(x) == m.Groups["y"].ValueSpan[0] ? 1 : -1;
        float angle = Normalize(theta + (toward * 4 * n));
        return m.Groups["lane"].ValueSpan[0] == 'A' ? (angle, 640 + (40 * k)) : (angle, 560 - (40 * k));
    }

    private static float Theta(char start) => start switch
    {
        'W' => -90f,
        'H' => 0f,
        'Z' => 90f,
        _ => 180f,
    };

    /// <summary>The next start round the wheel by increasing angle: Warrior, Hunter, Wizard, Cleric, Warrior.</summary>
    private static char Next(char start) => start switch
    {
        'W' => 'H',
        'H' => 'Z',
        'Z' => 'C',
        _ => 'W',
    };

    private static float Normalize(float degrees) =>
        degrees > 180f ? degrees - 360f : degrees <= -180f ? degrees + 360f : degrees;

    // A label: hub-X, X-start, Xhub-Rn, XY-joint or XY-Ln, any of the last three with a branch node .ck.
    [GeneratedRegex(
        @"^(?:hub-(?<hub>[WHZC])|(?<x>[WHZC])(?:-start|hub-(?<route>[SLR])(?<n>\d+)|(?<y>[WHZC])-(?:joint|(?<lane>[AB])(?<n>\d+))))(?:\.c(?<k>\d+))?$",
        RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LabelPattern();
}
