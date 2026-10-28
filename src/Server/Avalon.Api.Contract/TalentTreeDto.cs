using Avalon.Common;

namespace Avalon.Api.Contract;

/// <summary>
/// The shared talent tree (#1181) as anyone reads it: every node, every edge once and the node each class starts on.
/// Ids are surrogates that mean nothing; a node's place on the wheel is <see cref="TalentNodeDto.Angle" /> and
/// <see cref="TalentNodeDto.Radius" />.
/// </summary>
public sealed class TalentTreeDto
{
    /// <summary>In id order.</summary>
    public List<TalentNodeDto> Nodes { get; set; } = [];
    /// <summary>Undirected, each once with the smaller id first, by (NodeA, NodeB).</summary>
    public List<TalentEdgeDto> Edges { get; set; } = [];
    /// <summary>One per class.</summary>
    public List<TalentClassStartDto> ClassStarts { get; set; } = [];
}

public sealed class TalentNodeDto
{
    public int Id { get; set; }
    public TalentNodeKind Kind { get; set; }
    /// <summary>Set on a start, a notable, an ability, a capstone and a keystone; null on a small node or a junction.</summary>
    public string? Name { get; set; }
    /// <summary>In degrees, in (-180, 180].</summary>
    public float Angle { get; set; }
    /// <summary>In layout units.</summary>
    public float Radius { get; set; }
    /// <summary>
    /// The capstone fork the node belongs to, of which a character holds one at most; null on every other node.
    /// </summary>
    public int? ForkGroupId { get; set; }
    /// <summary>The ability a start or an ability node grants; null on every other node.</summary>
    public uint? AbilityId { get; set; }
    /// <summary>The rule a keystone bends; null on every other node, and on a keystone of stat lines only.</summary>
    public KeystoneRule? Keystone { get; set; }
    /// <summary>By (Stat, Kind).</summary>
    public List<TalentNodeStatDto> Stats { get; set; } = [];
    /// <summary>How a capstone reshapes the ability its branch hangs from, by kind; empty on every other node.</summary>
    public List<TalentAbilityModifierDto> Modifiers { get; set; } = [];
}

public sealed class TalentAbilityModifierDto
{
    public TalentModifierKind Kind { get; set; }
    /// <summary>The ability the capstone shapes.</summary>
    public uint AbilityId { get; set; }
    public int Value { get; set; }
    /// <summary>An explosion's effectiveness, %; null on every other kind.</summary>
    public int? Value2 { get; set; }
}

public sealed class TalentNodeStatDto
{
    public StatType Stat { get; set; }
    public TalentStatKind Kind { get; set; }
    public int Value { get; set; }
    /// <summary>The high end of an "adds X to Y" line; null on every other line.</summary>
    public int? ValueMax { get; set; }
}

public sealed class TalentEdgeDto
{
    public int NodeA { get; set; }
    public int NodeB { get; set; }
}

public sealed class TalentClassStartDto
{
    public CharacterClass Class { get; set; }
    public int NodeId { get; set; }
}
