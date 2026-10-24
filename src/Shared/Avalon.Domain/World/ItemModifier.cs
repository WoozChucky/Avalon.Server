namespace Avalon.Domain.World;

/// <summary>
/// One row of the modifier catalog (#1179): a tier of an affix family, or the single row of a unique's or a base's
/// implicit group (no tier). What it gives is its <see cref="Stats" /> lines, each a range a copy rolls within; where it
/// may roll is its <see cref="Weights" /> by base kind (a unique or an implicit has none: it is linked to its templates
/// instead, through <see cref="ItemTemplateModifier" />).
/// </summary>
public class ItemModifier
{
    public int Id { get; set; }

    public int GroupId { get; set; }

    public ItemModifierGroup Group { get; set; } = null!;

    /// <summary>1 to 5 on a prefix or suffix row; null on a unique or implicit row.</summary>
    public byte? Tier { get; set; }

    /// <summary>The item level a drop needs to roll this row; 1 for a unique or an implicit.</summary>
    public ushort RequiredItemLevel { get; set; }

    public List<ItemModifierStat> Stats { get; set; } = [];

    public List<ItemModifierWeight> Weights { get; set; } = [];
}

/// <summary>
/// One stat line of a catalog modifier: the stat and the range a copy rolls its value in. An "adds X to Y" line has a
/// second range, <see cref="Min2" /> to <see cref="Max2" />, for its high end; every other line has none.
/// </summary>
public class ItemModifierStat
{
    public int ModifierId { get; set; }

    /// <summary>Its place among the modifier's lines; an item's rolled value of this line carries the same index.</summary>
    public byte Index { get; set; }

    public StatType Stat { get; set; }

    public int Min { get; set; }

    public int Max { get; set; }

    public int? Min2 { get; set; }

    public int? Max2 { get; set; }
}

/// <summary>
/// How likely a prefix or suffix row is to roll on one base kind: its weight against the other eligible rows'. A kind
/// with no row, or weight 0, never rolls it.
/// </summary>
public class ItemModifierWeight
{
    public int ModifierId { get; set; }

    public ItemBaseKind BaseKind { get; set; }

    public int Weight { get; set; }
}
