using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Items;

namespace Avalon.World.Inventory;

/// <summary>
/// Modifier and base-stat entries for a caller that sends item entries again and again (#1179), so describing an item's
/// modifiers and base stats makes no garbage. Two ways in:
/// <list type="bullet">
/// <item>a caller that keeps its messages (the trade window, a kill's loot messages) refills an entry's list in place
/// (<see cref="Refill" />, <see cref="RefillBaseStats" />), and whatever a smaller item leaves over (a list, a modifier, a
/// value, a base stat) waits here for the next larger one;</item>
/// <item>a caller that builds its entries afresh (the tick's inventory update, an item request's refusal, the bank, the
/// select snapshot) takes the lists from here (<see cref="Rent" />, <see cref="RentBaseStats" />) and gives them back
/// (<see cref="Return(ItemSlotDto?)" />) once the message is encoded, which the packet's Create does before it returns.</item>
/// </list>
/// Exactly the item's modifiers and base stats are sent: nothing left over stays in an entry. Owned by one caller on one
/// thread.
/// </summary>
public sealed class ItemModifierDtoPool
{
    /// <summary>
    /// <paramref name="modifiers" /> in a list from this pool, null for none: for an entry built just now, whose list is
    /// given back with <see cref="Return(ItemSlotDto?)" /> once its message is encoded.
    /// </summary>
    public List<ItemModifierDto>? Rent(IReadOnlyList<ItemInstanceModifier> modifiers) => Refill(null, modifiers);

    /// <summary>
    /// <paramref name="baseStats" /> in a list from this pool, null for none: for an entry built just now, whose list is
    /// given back with <see cref="Return(ItemSlotDto?)" /> once its message is encoded.
    /// </summary>
    public List<ItemBaseStatDto>? RentBaseStats(in ItemBaseStats baseStats) => RefillBaseStats(null, baseStats);

    /// <summary>
    /// The entry's modifier and base-stat lists back to the pool, once the message that carried them is encoded; it is left
    /// with neither.
    /// </summary>
    public void Return(ItemSlotDto? entry)
    {
        if (entry is null)
            return;

        if (entry.Modifiers is { } modifiers)
        {
            Return(modifiers);
            entry.Modifiers = null;
        }

        if (entry.BaseStats is { } baseStats)
        {
            Return(baseStats);
            entry.BaseStats = null;
        }
    }

    /// <summary>
    /// Every entry's modifier and base-stat lists back to the pool (<see cref="Return(ItemSlotDto?)" />), once their
    /// message is encoded.
    /// </summary>
    public void Return(ReadOnlySpan<InventorySlotUpdateDto> entries)
    {
        for (int i = 0; i < entries.Length; i++)
            Return(entries[i].Item);
    }

    private readonly Stack<List<ItemModifierDto>> _lists = new();
    private readonly Stack<ItemModifierDto> _modifiers = new();
    private readonly Stack<ItemModifierValueDto> _values = new();
    private readonly Stack<List<ItemBaseStatDto>> _baseStatLists = new();
    private readonly Stack<ItemBaseStatDto> _baseStats = new();

    /// <summary>
    /// <paramref name="current" /> (an entry's list, or null) refilled with exactly <paramref name="modifiers" />: the
    /// list to send, null for none, with the one it replaced kept here.
    /// </summary>
    public List<ItemModifierDto>? Refill(List<ItemModifierDto>? current, IReadOnlyList<ItemInstanceModifier> modifiers)
    {
        if (modifiers.Count == 0)
        {
            if (current is not null)
                Return(current);
            return null;
        }

        List<ItemModifierDto> target = current ?? (_lists.TryPop(out List<ItemModifierDto>? kept) ? kept : []);
        while (target.Count > modifiers.Count)
        {
            Return(target[^1]);
            target.RemoveAt(target.Count - 1);
        }

        for (int i = 0; i < modifiers.Count; i++)
        {
            if (i == target.Count)
                target.Add(_modifiers.TryPop(out ItemModifierDto? modifier) ? modifier : new ItemModifierDto());

            ItemModifierDto entry = target[i];
            entry.ModifierId = (uint)modifiers[i].ModifierId;
            RefillValues(entry.Values, modifiers[i].Values);
        }

        return target;
    }

    /// <summary>
    /// <paramref name="current" /> (an entry's list, or null) refilled with exactly <paramref name="baseStats" />, in
    /// base-stat order: the list to send, null for none, with the one it replaced kept here.
    /// </summary>
    public List<ItemBaseStatDto>? RefillBaseStats(List<ItemBaseStatDto>? current, in ItemBaseStats baseStats)
    {
        if (baseStats.IsEmpty)
        {
            if (current is not null)
                Return(current);
            return null;
        }

        List<ItemBaseStatDto> target = current ?? (_baseStatLists.TryPop(out List<ItemBaseStatDto>? kept) ? kept : []);
        int count = 0;
        foreach ((ItemBaseStat stat, int value) in baseStats)
        {
            if (count == target.Count)
                target.Add(_baseStats.TryPop(out ItemBaseStatDto? entry) ? entry : new ItemBaseStatDto());

            target[count].Stat = (BaseStat)stat;
            target[count].Value = value;
            count++;
        }

        while (target.Count > count)
        {
            _baseStats.Push(target[^1]);
            target.RemoveAt(target.Count - 1);
        }

        return target;
    }

    private void RefillValues(List<ItemModifierValueDto> target, List<ItemInstanceModifierValue> values)
    {
        while (target.Count > values.Count)
        {
            _values.Push(target[^1]);
            target.RemoveAt(target.Count - 1);
        }

        for (int i = 0; i < values.Count; i++)
        {
            if (i == target.Count)
                target.Add(_values.TryPop(out ItemModifierValueDto? value) ? value : new ItemModifierValueDto());

            ItemModifierDtoMapper.Fill(target[i], values[i]);
        }
    }

    private void Return(List<ItemModifierDto> list)
    {
        for (int i = 0; i < list.Count; i++)
            Return(list[i]);

        list.Clear();
        _lists.Push(list);
    }

    private void Return(List<ItemBaseStatDto> list)
    {
        for (int i = 0; i < list.Count; i++)
            _baseStats.Push(list[i]);

        list.Clear();
        _baseStatLists.Push(list);
    }

    private void Return(ItemModifierDto modifier)
    {
        List<ItemModifierValueDto> values = modifier.Values;
        for (int i = 0; i < values.Count; i++)
            _values.Push(values[i]);

        values.Clear();
        _modifiers.Push(modifier);
    }
}
