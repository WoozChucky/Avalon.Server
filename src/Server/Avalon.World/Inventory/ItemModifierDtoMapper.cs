using Avalon.Domain.World;
using Avalon.Network.Packets.Items;

namespace Avalon.World.Inventory;

/// <summary>
/// An item's modifiers and base stats on the wire (#1179), the same for a slot, a trade window entry and a drop. An
/// unidentified copy sends its base stats and its implicit lines, and none of its prefixes, suffixes or unique lines.
/// </summary>
public static class ItemModifierDtoMapper
{
    // How many modifiers' answers ToDtos keeps on the stack.
    private const int StackAnswers = 16;

    /// <summary>
    /// The modifiers a client is shown (<see cref="ItemVisibility.Shows" />), in list order, each one's values by index;
    /// null for none, so an item without modifiers costs no list. Built once per message: each modifier is asked once,
    /// its answer kept on the stack, and the list is sized by how many are shown. The pooled path
    /// (<see cref="ItemModifierDtoPool.Refill" />) is the per-tick one.
    /// </summary>
    /// <param name="catalog">The current generation's catalog: what kind each modifier of an unidentified copy is.</param>
    /// <param name="identified">Whether the copy is identified; an unidentified one shows its implicit lines only.</param>
    /// <param name="itemLevel">The copy's item level: the threshold a sealed affix it shows wakes at (#1180).</param>
    public static List<ItemModifierDto>? ToDtos(IReadOnlyList<ItemInstanceModifier> modifiers,
        ItemModifierCatalog catalog, bool identified, ushort itemLevel)
    {
        int total = modifiers.Count;
        if (total == 0)
            return null;

        // A copy carries a handful of modifiers; a longer list (none is made today) asks the heap for its answers.
        Span<bool> shows = total <= StackAnswers ? stackalloc bool[StackAnswers] : new bool[total];
        int count = 0;
        for (int i = 0; i < total; i++)
        {
            shows[i] = ItemVisibility.Shows(catalog, modifiers[i].ModifierId, identified);
            if (shows[i])
                count++;
        }

        if (count == 0)
            return null;

        var dtos = new List<ItemModifierDto>(count);
        for (int i = 0; i < total; i++)
        {
            if (!shows[i])
                continue;

            // The entry's own list, sized once: replacing it would throw away the one its initializer made.
            ItemInstanceModifier modifier = modifiers[i];
            var dto = new ItemModifierDto { ModifierId = (uint)modifier.ModifierId };
            FillSeal(dto, modifier, identified, itemLevel);
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
    /// A shown modifier's seal (#1180), written over whatever <paramref name="target" /> held: its progress and the
    /// threshold for <paramref name="itemLevel" /> on a sealed row of an identified copy, and neither on an awake one, so
    /// a reused entry never keeps an earlier row's seal. The caller has already asked <see cref="ItemVisibility.Shows" />,
    /// and a sealed affix is a prefix or a suffix, which an unidentified copy never shows; an unidentified copy still
    /// sends no seal on any line it shows, whatever wrote the seal.
    /// </summary>
    internal static void FillSeal(ItemModifierDto target, ItemInstanceModifier modifier, bool identified, ushort itemLevel)
    {
        if (identified && modifier.SealProgress is { } progress)
        {
            target.SealProgress = (uint)progress;
            target.SealThreshold = (uint)ItemAwakening.Threshold(itemLevel);
        }
        else
        {
            target.SealProgress = null;
            target.SealThreshold = null;
        }
    }

    /// <summary>One rolled value, written over whatever <paramref name="target" /> held.</summary>
    internal static void Fill(ItemModifierValueDto target, ItemInstanceModifierValue value)
    {
        target.Stat = (ItemStat)value.Stat;
        target.Value = value.Value;
        target.Value2 = value.Value2;
    }
}
