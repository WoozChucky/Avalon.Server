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

    /// <summary>A deep copy, with copies of the value rows: what a save takes on the tick, so the write shares nothing the tick changes.</summary>
    public ItemInstanceModifier Copy()
    {
        var values = new List<ItemInstanceModifierValue>(Values.Count);
        foreach (ItemInstanceModifierValue value in Values)
        {
            values.Add(new ItemInstanceModifierValue
            {
                ItemInstanceModifierId = value.ItemInstanceModifierId,
                Index = value.Index,
                Stat = value.Stat,
                Value = value.Value,
                Value2 = value.Value2,
            });
        }

        return new ItemInstanceModifier
        {
            Id = Id,
            ItemInstanceId = ItemInstanceId,
            ModifierId = ModifierId,
            Ordinal = Ordinal,
            Values = values,
        };
    }
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
}
