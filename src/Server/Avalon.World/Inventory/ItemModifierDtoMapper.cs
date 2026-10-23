using Avalon.Domain.World;
using Avalon.Network.Packets.Items;

namespace Avalon.World.Inventory;

/// <summary>An item's modifiers on the wire (#1179), the same for a slot and a trade window entry.</summary>
public static class ItemModifierDtoMapper
{
    /// <summary>
    /// The modifiers in list order, each one's values by index; null for none, so an item without modifiers costs no list.
    /// </summary>
    public static List<ItemModifierDto>? ToDtos(IReadOnlyList<ItemInstanceModifier> modifiers)
    {
        if (modifiers.Count == 0)
            return null;

        var dtos = new List<ItemModifierDto>(modifiers.Count);
        for (int i = 0; i < modifiers.Count; i++)
        {
            ItemInstanceModifier modifier = modifiers[i];
            var dto = new ItemModifierDto
            {
                ModifierId = modifier.ModifierId,
                Values = new List<ItemModifierValueDto>(modifier.Values.Count),
            };
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

    /// <summary>One rolled value, written over whatever <paramref name="target" /> held.</summary>
    internal static void Fill(ItemModifierValueDto target, ItemInstanceModifierValue value)
    {
        target.Stat = (ItemStat)value.Stat;
        target.Value = value.Value;
        target.Value2 = value.Value2;
    }
}
