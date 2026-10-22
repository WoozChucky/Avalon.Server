using Avalon.Common.ValueObjects;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Trading;

namespace Avalon.World.Inventory;

/// <summary>
/// What one character has offered in a face-to-face trade (#1175): the item instances, which stay in its Bag but are
/// locked, and the gold, which stays in its balance but is held. A locked instance cannot be moved, split, merged into,
/// swapped with, banked, equipped, destroyed, sold, used or consumed; adds never fill its stack, and removals by
/// template never see it. Held gold cannot be spent: what can is <see cref="Spendable" />. Every path that consults it
/// asks <see cref="Any" /> first, one bool read, so nothing changes, and nothing allocates, while nothing is offered.
/// World-side and never saved. Tick thread only.
/// </summary>
public sealed class InventoryLocks
{
    /// <summary>Made on first use, sized for a full trade, and kept.</summary>
    private List<ItemInstanceId>? _locked;

    private ulong _heldGold;

    /// <summary>Whether any instance is locked or any gold held.</summary>
    public bool Any { get; private set; }

    /// <summary>Gold offered in the trade: part of the balance, but not spendable.</summary>
    public ulong HeldGold
    {
        get => _heldGold;
        set
        {
            _heldGold = value;
            Refresh();
        }
    }

    public bool IsLocked(ItemInstanceId id)
    {
        if (_locked is not { } locked)
            return false;

        for (int i = 0; i < locked.Count; i++)
        {
            if (locked[i].Value == id.Value)
                return true;
        }

        return false;
    }

    /// <summary>Locking an instance already locked changes nothing.</summary>
    public void Lock(ItemInstanceId id)
    {
        if (IsLocked(id))
            return;

        (_locked ??= new List<ItemInstanceId>(TradeRules.MaxItems)).Add(id);
        Any = true;
    }

    public void Unlock(ItemInstanceId id)
    {
        if (_locked is not { } locked)
            return;

        for (int i = 0; i < locked.Count; i++)
        {
            if (locked[i].Value == id.Value)
            {
                locked.RemoveAt(i);
                break;
            }
        }

        Refresh();
    }

    /// <summary>The balance less the held gold, never below 0.</summary>
    public ulong Spendable(ulong balance) => balance > _heldGold ? balance - _heldGold : 0;

    /// <summary>How many of the item the container holds in stacks that are not locked.</summary>
    public long UnlockedCount(CharacterInventoryContainer container, ItemTemplateId template)
    {
        long count = 0;
        foreach (InventoryItem stack in container.Held)
        {
            if (stack.TemplateId.Value == template.Value && !IsLocked(stack.InstanceId))
                count += stack.Count;
        }

        return count;
    }

    /// <summary>Unlocks every instance and holds no gold.</summary>
    public void Clear()
    {
        _locked?.Clear();
        _heldGold = 0;
        Any = false;
    }

    private void Refresh() => Any = _heldGold > 0 || _locked is { Count: > 0 };
}
