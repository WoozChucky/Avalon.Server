using Avalon.Domain.World;
using Avalon.Network.Packets.Items;
using Avalon.World.Public.Characters;

namespace Avalon.World.Inventory;

/// <summary>
/// What an item is beyond its template, on the wire (#1179): its rarity, item level and modifiers, the same for a slot
/// and a trade window entry.
/// </summary>
public static class ItemDetailsDtoMapper
{
    /// <summary>A new details entry: the modifiers in list order, each one's values by index.</summary>
    public static ItemDetailsDto ToDto(InventoryItem item, IReadOnlyList<ItemInstanceModifier> modifiers)
    {
        var details = new ItemDetailsDto { Rarity = item.Rarity, ItemLevel = item.ItemLevel };
        Fill(details.Modifiers, modifiers);
        return details;
    }

    /// <summary>
    /// <paramref name="target" /> refilled in place, for a caller that sends the same entries again (the trade window):
    /// the entries it already holds are overwritten, so re-sending the same item allocates nothing, and only a modifier
    /// or value past what it showed last is allocated.
    /// </summary>
    public static void Refill(ItemDetailsDto target, InventoryItem item, IReadOnlyList<ItemInstanceModifier> modifiers)
    {
        target.Rarity = item.Rarity;
        target.ItemLevel = item.ItemLevel;
        Fill(target.Modifiers, modifiers);
    }

    /// <summary>Exactly the modifiers' entries: the ones already there overwritten, the rest added.</summary>
    private static void Fill(List<ItemModifierDto> target, IReadOnlyList<ItemInstanceModifier> modifiers)
    {
        Resize(target, modifiers.Count);
        for (int i = 0; i < modifiers.Count; i++)
        {
            ItemInstanceModifier modifier = modifiers[i];
            if (i == target.Count)
                target.Add(new ItemModifierDto());

            ItemModifierDto entry = target[i];
            entry.ModifierId = modifier.ModifierId;
            FillValues(entry.Values, modifier.Values);
        }
    }

    private static void FillValues(List<ItemModifierValueDto> target, List<ItemInstanceModifierValue> values)
    {
        Resize(target, values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            ItemInstanceModifierValue value = values[i];
            if (i == target.Count)
                target.Add(new ItemModifierValueDto());

            ItemModifierValueDto entry = target[i];
            entry.Stat = (ItemStat)value.Stat;
            entry.Value = value.Value;
            entry.Value2 = value.Value2;
        }
    }

    /// <summary>Drops the entries past <paramref name="count" />, or makes room for all of them in one step.</summary>
    private static void Resize<T>(List<T> target, int count)
    {
        if (target.Count > count)
            target.RemoveRange(count, target.Count - count);
        else if (target.Capacity < count)
            target.Capacity = count;
    }
}
