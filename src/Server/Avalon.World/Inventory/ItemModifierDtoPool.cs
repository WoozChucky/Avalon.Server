using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Items;

namespace Avalon.World.Inventory;

/// <summary>
/// Modifier entries for a caller that sends item entries again and again (#1179), so describing an item's modifiers
/// makes no garbage. Two ways in:
/// <list type="bullet">
/// <item>a caller that keeps its messages (the trade window, a kill's loot messages) refills an entry's list in place
/// (<see cref="Refill" />), and whatever a smaller item leaves over (a list, a modifier, a value) waits here for the next
/// larger one;</item>
/// <item>a caller that builds its entries afresh (the tick's inventory update, an item request's refusal, the bank, the
/// select snapshot) takes the lists from here (<see cref="Rent" />) and gives them back (<see cref="Return(ItemSlotDto?)" />)
/// once the message is encoded, which the packet's Create does before it returns.</item>
/// </list>
/// Exactly the item's modifiers are sent: nothing left over stays in an entry. Owned by one caller on one thread.
/// </summary>
public sealed class ItemModifierDtoPool
{
    /// <summary>
    /// <paramref name="modifiers" /> in a list from this pool, null for none: for an entry built just now, whose list is
    /// given back with <see cref="Return(ItemSlotDto?)" /> once its message is encoded.
    /// </summary>
    public List<ItemModifierDto>? Rent(IReadOnlyList<ItemInstanceModifier> modifiers) => Refill(null, modifiers);

    /// <summary>The entry's modifier list back to the pool, once the message that carried it is encoded; it is left with none.</summary>
    public void Return(ItemSlotDto? entry)
    {
        if (entry?.Modifiers is not { } list)
            return;

        Return(list);
        entry.Modifiers = null;
    }

    /// <summary>Every entry's modifier lists back to the pool (<see cref="Return(ItemSlotDto?)" />), once their message is encoded.</summary>
    public void Return(ReadOnlySpan<InventorySlotUpdateDto> entries)
    {
        for (int i = 0; i < entries.Length; i++)
            Return(entries[i].Item);
    }

    private readonly Stack<List<ItemModifierDto>> _lists = new();
    private readonly Stack<ItemModifierDto> _modifiers = new();
    private readonly Stack<ItemModifierValueDto> _values = new();

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

    private void Return(ItemModifierDto modifier)
    {
        List<ItemModifierValueDto> values = modifier.Values;
        for (int i = 0; i < values.Count; i++)
            _values.Push(values[i]);

        values.Clear();
        _modifiers.Push(modifier);
    }
}
