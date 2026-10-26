using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>
/// One modifier an item instance carries (#1179): which catalog modifier it rolled (<see cref="ModifierId" /> names a row
/// of ItemModifiers in the World database, so there is no navigation and no foreign key) and where it sits among the
/// item's modifiers. The rolled numbers are its <see cref="Values" />, each naming its stat, so a catalog edit never
/// changes an item that already exists. Modifiers change over an item's life, so each has its own id, which the world
/// server allocates, never the database.
/// </summary>
public class ItemInstanceModifier
{
    public Guid Id { get; set; }

    public ItemInstanceId ItemInstanceId { get; set; } = default!;

    public int ModifierId { get; set; }

    /// <summary>Its place among the item's modifiers, the order they are shown in.</summary>
    public byte Ordinal { get; set; }

    public List<ItemInstanceModifierValue> Values { get; set; } = [];

    /// <summary>
    /// Set exactly on a sealed affix (#1180): the points its bar holds toward waking, at least 0 and at most the item's
    /// <see cref="ItemAwakening.Threshold" />. Null on an awake affix, which every other row is. An item carries at most one
    /// seal (the database's partial unique index), and a sealed row adds nothing to stats or Resonance until it wakes.
    /// </summary>
    public int? SealProgress { get; set; }

    /// <summary>Whether this affix is sealed: <see cref="SealProgress" /> is set. Not a column of its own.</summary>
    public bool Sealed => SealProgress is not null;

    /// <summary>
    /// A copy of the row alone, its <see cref="Values" /> its own empty list: what a save takes on the tick beside copies
    /// of the value rows (<see cref="ItemInstanceModifierValue.Copy" />), so the write shares nothing the tick changes and
    /// every value is a row of its own.
    /// </summary>
    public ItemInstanceModifier CopyRow() => new()
    {
        Id = Id,
        ItemInstanceId = ItemInstanceId,
        ModifierId = ModifierId,
        Ordinal = Ordinal,
        SealProgress = SealProgress,
    };
}

/// <summary>
/// One rolled number of an item's modifier: the stat it gives and how much. <see cref="Value2" /> is set exactly on an
/// "adds X to Y" line, as its high end; every other line is the single <see cref="Value" />.
/// </summary>
public class ItemInstanceModifierValue
{
    public Guid ItemInstanceModifierId { get; set; }

    /// <summary>Its place among the modifier's values, matching the catalog's stat line.</summary>
    public byte Index { get; set; }

    public StatType Stat { get; set; }

    public int Value { get; set; }

    public int? Value2 { get; set; }

    /// <summary>A copy of the row, which a save writes beside <see cref="ItemInstanceModifier.CopyRow" />.</summary>
    public ItemInstanceModifierValue Copy() => new()
    {
        ItemInstanceModifierId = ItemInstanceModifierId,
        Index = Index,
        Stat = Stat,
        Value = Value,
        Value2 = Value2,
    };
}
