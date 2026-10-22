using Avalon.Common.ValueObjects;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Trading;

namespace Avalon.World.Inventory;

/// <summary>
/// What one character has offered in a face-to-face trade (#1175): the item instances, which stay in its Bag but are
/// locked, and the gold, which stays in its balance but is held. A locked instance cannot be moved, split, merged into,
/// swapped with, banked, equipped, destroyed, sold, used or consumed; adds never fill its stack, and removals by
/// template never see it. Held gold cannot be spent: what can is <see cref="Spendable" />.
/// While the trade completes, it also holds what the character is about to receive: the Bag slots the incoming items
/// go to are reserved, and the incoming gold and copies of each template are expected. Every add but the trade's own
/// treats a reserved slot as taken (<see cref="IsFree" />, <see cref="FreeSlotCount(CharacterInventoryContainer)" />),
/// counts <see cref="IncomingCount" /> toward a Unique template's one copy, and counts <see cref="IncomingGold" />
/// toward the gold cap (<see cref="CanReceiveGold" />).
/// Every path that consults it asks <see cref="Any" /> first (or a check here that does), one bool read, so nothing
/// changes, and nothing allocates, while nothing is offered or reserved. World-side and never saved. Tick thread only.
/// </summary>
public sealed class InventoryLocks
{
    /// <summary>Made on first use, sized for a full trade, and kept.</summary>
    private List<ItemInstanceId>? _locked;

    /// <summary>Made on first use, sized for a full trade, and kept: one entry per incoming template.</summary>
    private List<IncomingCopies>? _incoming;

    private ulong _heldGold;

    private ulong _incomingGold;

    /// <summary>One bit per Bag slot; the Bag has 30.</summary>
    private ulong _reserved;

    /// <summary>Whether any instance is locked, any gold held, or anything reserved or expected.</summary>
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

    /// <summary>Gold a completing trade is about to pay the character: counted toward the cap by every other add.</summary>
    public ulong IncomingGold
    {
        get => _incomingGold;
        set
        {
            _incomingGold = value;
            Refresh();
        }
    }

    /// <summary>One bool read while nothing is offered or reserved.</summary>
    public bool IsLocked(ItemInstanceId id) => Any && Contains(id);

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

    /// <summary>Whether a completing trade will put an item in this Bag slot. A bit test.</summary>
    public bool IsReserved(byte bagSlot) => bagSlot < 64 && (_reserved & (1UL << bagSlot)) != 0;

    /// <summary>Reserves a Bag slot for a completing trade's incoming item. Reserving it again changes nothing.</summary>
    public void Reserve(byte bagSlot)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bagSlot, (byte)64);
        _reserved |= 1UL << bagSlot;
        Any = true;
    }

    /// <summary>Empty and not reserved: a slot any add but the trade's own may use.</summary>
    public bool IsFree(CharacterInventoryContainer bag, byte slot) => !bag.TryGet(slot, out _) && !IsReserved(slot);

    /// <summary>The Bag's empty slots less those reserved.</summary>
    public int FreeSlotCount(CharacterInventoryContainer bag)
    {
        int free = bag.Capacity - bag.Held.Count;
        if (_reserved == 0)
            return free;

        for (byte slot = 0; slot < bag.Capacity; slot++)
        {
            if (IsReserved(slot) && !bag.TryGet(slot, out _))
                free--;
        }

        return free;
    }

    /// <summary>The empty slots of a Bag as a rule plans it (<paramref name="bag" /> by slot), less those reserved.</summary>
    public int FreeSlotCount(IReadOnlyDictionary<byte, InventoryItem> bag, byte capacity)
    {
        int free = capacity - bag.Count;
        if (_reserved == 0)
            return free;

        for (byte slot = 0; slot < capacity; slot++)
        {
            if (IsReserved(slot) && !bag.ContainsKey(slot))
                free--;
        }

        return free;
    }

    /// <summary>Records <paramref name="count" /> more of the template on its way in from a completing trade.</summary>
    public void ExpectIncoming(ItemTemplateId template, uint count)
    {
        if (count == 0)
            return;

        List<IncomingCopies> incoming = _incoming ??= new List<IncomingCopies>(TradeRules.MaxItems);
        for (int i = 0; i < incoming.Count; i++)
        {
            if (incoming[i].Template.Value == template.Value)
            {
                incoming[i] = incoming[i] with { Count = incoming[i].Count + count };
                Any = true;
                return;
            }
        }

        incoming.Add(new IncomingCopies(template, count));
        Any = true;
    }

    /// <summary>How many of the template a completing trade is about to add: counted toward Unique by every other add.</summary>
    public long IncomingCount(ItemTemplateId template)
    {
        if (_incoming is not { } incoming)
            return 0;

        for (int i = 0; i < incoming.Count; i++)
        {
            if (incoming[i].Template.Value == template.Value)
                return incoming[i].Count;
        }

        return 0;
    }

    /// <summary>
    /// Whether <paramref name="amount" /> more gold stays within <paramref name="maxGold" />, counting the gold a completing
    /// trade is about to pay as already in the balance: IWallet.TryAddGold's rule, without wrapping. A balance (with the
    /// incoming gold) already above a lowered cap refuses everything, and one at the cap everything but 0.
    /// </summary>
    public bool CanReceiveGold(ulong balance, ulong amount, ulong maxGold)
    {
        ulong expected = balance > ulong.MaxValue - _incomingGold ? ulong.MaxValue : balance + _incomingGold;
        return expected <= maxGold && amount <= maxGold - expected;
    }

    /// <summary>Unlocks every instance, holds no gold, and reserves and expects nothing.</summary>
    public void Clear()
    {
        _locked?.Clear();
        _incoming?.Clear();
        _heldGold = 0;
        _incomingGold = 0;
        _reserved = 0;
        Any = false;
    }

    private bool Contains(ItemInstanceId id)
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

    private void Refresh() =>
        Any = _heldGold > 0 || _incomingGold > 0 || _reserved != 0 || _locked is { Count: > 0 } || _incoming is { Count: > 0 };

    private readonly record struct IncomingCopies(ItemTemplateId Template, long Count);
}
