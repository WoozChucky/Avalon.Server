using Avalon.Domain.World;
using Avalon.Network.Packets.Items;

namespace Avalon.World.Inventory;

/// <summary>
/// An item's modifiers and base stats on the wire (#1179), the same for a slot, a trade window entry and a drop. An
/// unidentified copy sends its base stats and its implicit lines, and none of its prefixes, suffixes or unique lines.
/// </summary>
public static class ItemModifierDtoMapper
{
    /// <summary>
    /// The modifiers a client is shown (<see cref="ItemVisibility.Shows" />), in list order, each one's values by index;
    /// null for none, so an item without modifiers costs no list. Built once per message, so it counts the shown ones
    /// first and sizes the list by that count; the pooled path (<see cref="ItemModifierDtoPool.Refill" />) looks each one
    /// up once.
    /// </summary>
    /// <param name="catalog">The current generation's catalog: what kind each modifier of an unidentified copy is.</param>
    /// <param name="identified">Whether the copy is identified; an unidentified one shows its implicit lines only.</param>
    public static List<ItemModifierDto>? ToDtos(IReadOnlyList<ItemInstanceModifier> modifiers,
        ItemModifierCatalog catalog, bool identified)
    {
        int count = ShownCount(modifiers, catalog, identified);
        if (count == 0)
            return null;

        var dtos = new List<ItemModifierDto>(count);
        for (int i = 0; i < modifiers.Count; i++)
        {
            ItemInstanceModifier modifier = modifiers[i];
            if (!ItemVisibility.Shows(catalog, modifier.ModifierId, identified))
                continue;

            // The entry's own list, sized once: replacing it would throw away the one its initializer made.
            var dto = new ItemModifierDto { ModifierId = (uint)modifier.ModifierId };
            dto.Values.Capacity = modifier.Values.Count;
            for (int v = 0; v < modifier.Values.Count; v++)
            {
                var value = new ItemModifierValueDto();
                Fill(value, modifier.Values[v]);
                dto.Values.Add(value);
            }

            dtos.Add(dto);
        }

        return dtos;
    }

    /// <summary>The base stats in base-stat order; null for none, so an item without them costs no list.</summary>
    public static List<ItemBaseStatDto>? ToBaseStatDtos(in ItemBaseStats baseStats)
    {
        if (baseStats.IsEmpty)
            return null;

        var dtos = new List<ItemBaseStatDto>();
        foreach ((ItemBaseStat stat, int value) in baseStats)
            dtos.Add(new ItemBaseStatDto { Stat = (BaseStat)stat, Value = value });

        return dtos;
    }

    /// <summary>
    /// How many of <paramref name="modifiers" /> a client is shown: all of an identified copy's, counted without a lookup,
    /// and an unidentified copy's implicit lines (<see cref="ItemVisibility.Shows" />).
    /// </summary>
    private static int ShownCount(IReadOnlyList<ItemInstanceModifier> modifiers, ItemModifierCatalog catalog,
        bool identified)
    {
        if (identified)
            return modifiers.Count;

        int count = 0;
        for (int i = 0; i < modifiers.Count; i++)
        {
            if (ItemVisibility.ShowsUnidentified(catalog.Find(modifiers[i].ModifierId)))
                count++;
        }

        return count;
    }

    /// <summary>One rolled value, written over whatever <paramref name="target" /> held.</summary>
    internal static void Fill(ItemModifierValueDto target, ItemInstanceModifierValue value)
    {
        target.Stat = (ItemStat)value.Stat;
        target.Value = value.Value;
        target.Value2 = value.Value2;
    }
}
