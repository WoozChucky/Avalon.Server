using Avalon.Common;
using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>What a talent node is (#1181). Stored as a number; append-only.</summary>
public enum TalentNodeKind : byte
{
    /// <summary>A class's start: free, never allocated, and linked to the class's starter ability.</summary>
    Start = 1,

    Small = 2,
    Notable = 3,

    /// <summary>Unlocks the ability its link names.</summary>
    Ability = 4,

    Capstone = 5,
    Keystone = 6,

    /// <summary>A hub junction, where a class's spoke meets the hub ring.</summary>
    Junction = 7,
}

/// <summary>How a talent node's stat line counts (#1181). Stored as a number; append-only.</summary>
public enum TalentStatKind : byte
{
    /// <summary>Added to the gear's lines of the same stat, as a worn item's global line is.</summary>
    Added = 1,

    /// <summary>A multiplier of its own: "N % more".</summary>
    More = 2,
}

/// <summary>
/// The rule a keystone bends (#1181 Part D). Stored as a number; append-only. A keystone that only trades a gain for a
/// loss carries stat lines and no rule.
/// </summary>
public enum KeystoneRule : byte
{
    /// <summary>Blood Price: an ability's cost is paid from current life instead of mana.</summary>
    LifeCost = 1,

    /// <summary>Steady Hand: no hit, heal or aura tick of the holder's is ever critical.</summary>
    NoCrits = 2,

    /// <summary>Unbroken: the ward also absorbs blight and bleeding, and waits twice as long to recharge.</summary>
    WardTakesAll = 3,
}

/// <summary>
/// How a capstone reshapes the ability it hangs from (#1181 Part D): one of eight reusable modifiers, summed per ability
/// over the capstones a character holds. Stored as a number; append-only.
/// </summary>
public enum TalentModifierKind : byte
{
    /// <summary>Value more projectiles, fired in a fan 10 degrees apart; a unit is affected once per cast.</summary>
    AddedProjectiles = 1,

    /// <summary>
    /// Where the projectile ends on a unit, a circle of Value / 10 metres hits every other hostile unit in it at Value2 %
    /// of the amount.
    /// </summary>
    ImpactExplosion = 2,

    /// <summary>A circle's radius, and an explosion's, times (1 + Value / 100).</summary>
    AreaPct = 3,

    /// <summary>A cone's reach times (1 + Value / 100).</summary>
    ConeReachPct = 4,

    /// <summary>A cone's arc times (1 + Value / 100), at most 360 degrees.</summary>
    ConeArcPct = 5,

    /// <summary>The ability's aura may stack Value more times from this caster.</summary>
    AuraStacks = 6,

    /// <summary>The ability's aura lasts times (1 + Value / 100).</summary>
    AuraDurationPct = 7,

    /// <summary>Each unit affected is hit, or healed, again at once at Value % of the amount.</summary>
    SecondHit = 8,
}

/// <summary>
/// One node of the shared talent tree (#1181): a balance row with a literal id that means nothing. Where it sits on the
/// wheel is for the client to draw: <see cref="Angle" /> in degrees, in (-180, 180], and <see cref="Radius" /> in layout
/// units.
/// </summary>
public class TalentNode
{
    public int Id { get; set; }

    public TalentNodeKind Kind { get; set; }

    /// <summary>The node's name, required on a notable, an ability, a capstone and a keystone; small nodes have none.</summary>
    public string? Name { get; set; }

    public float Angle { get; set; }

    public float Radius { get; set; }

    /// <summary>The fork a capstone belongs to, of which a character holds one at most; null on every other node.</summary>
    public int? ForkGroupId { get; set; }

    /// <summary>The rule a keystone bends; null on every other node, and on a keystone that carries only stat lines.</summary>
    public KeystoneRule? Keystone { get; set; }
}

/// <summary>An undirected edge of the talent tree, written once with the smaller id first.</summary>
public class TalentEdge
{
    public int NodeA { get; set; }

    public int NodeB { get; set; }
}

/// <summary>
/// A talent node's stat line, in the gear's <see cref="StatType" /> vocabulary. An "adds X to Y" line carries its high
/// end in <see cref="ValueMax" />; every other line has none.
/// </summary>
public class TalentNodeStat
{
    public int NodeId { get; set; }

    public StatType Stat { get; set; }

    public TalentStatKind Kind { get; set; }

    public int Value { get; set; }

    public int? ValueMax { get; set; }
}

/// <summary>The ability a start or an ability node grants; an ability is granted by one node at most.</summary>
public class TalentNodeAbility
{
    public int NodeId { get; set; }

    public AbilityId AbilityId { get; set; } = default!;
}

/// <summary>
/// One of a capstone's modifiers (#1181 Part D): how it reshapes <see cref="AbilityId" />, the ability its branch hangs
/// from. A capstone holds one row per kind. <see cref="Value2" /> is set on an
/// <see cref="TalentModifierKind.ImpactExplosion" /> (its effectiveness, %) and on no other kind.
/// </summary>
public class TalentAbilityModifier
{
    public int NodeId { get; set; }

    public TalentModifierKind Kind { get; set; }

    public AbilityId AbilityId { get; set; } = default!;

    public int Value { get; set; }

    public int? Value2 { get; set; }
}

/// <summary>A fork of capstones, of which a character holds one at most.</summary>
public class TalentForkGroup
{
    public int Id { get; set; }
}

/// <summary>The node a class starts on: one per class, a <see cref="TalentNodeKind.Start" /> node.</summary>
public class TalentClassStart
{
    public CharacterClass Class { get; set; }

    public int NodeId { get; set; }
}

/// <summary>The talent tree's rows, as the seed, the repository, the catalog, the export and the API share them.</summary>
public sealed record TalentTreeRows(
    IReadOnlyList<TalentNode> Nodes,
    IReadOnlyList<TalentEdge> Edges,
    IReadOnlyList<TalentNodeStat> Stats,
    IReadOnlyList<TalentNodeAbility> Abilities,
    IReadOnlyList<TalentForkGroup> ForkGroups,
    IReadOnlyList<TalentClassStart> Starts)
{
    /// <summary>The capstones' modifiers, by (NodeId, Kind); none where the reader has no use for them.</summary>
    public IReadOnlyList<TalentAbilityModifier> Modifiers { get; init; } = [];

    /// <summary>
    /// The abilities the tree grants: each one a class start or a node links (#1181). Granted is what makes an ability a
    /// class ability, held to the class rules; an ability nothing grants is a creature's. Built anew on each read.
    /// </summary>
    public IReadOnlySet<AbilityId> Granted => Abilities.Select(a => a.AbilityId).ToHashSet();
}
