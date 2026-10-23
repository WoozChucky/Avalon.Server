using Avalon.Domain.World;
using Avalon.Network.Packets.Items;

namespace Avalon.World.Inventory;

/// <summary>
/// Modifier entries for a caller that sends the same item entries again and again (the trade window, #1179): an entry's
/// list is refilled in place, and whatever a smaller item leaves over (a list, a modifier, a value) waits here for the
/// next larger one, so neither re-sending an item nor the window shrinking and growing again makes garbage. Exactly the
/// item's modifiers are sent: nothing left over stays in an entry. Owned by one caller on one thread.
/// </summary>
public sealed class ItemModifierDtoPool
{
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
            entry.ModifierId = modifiers[i].ModifierId;
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
