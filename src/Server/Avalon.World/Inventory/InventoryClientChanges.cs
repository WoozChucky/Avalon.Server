using Avalon.World.Public.Enums;

namespace Avalon.World.Inventory;

/// <summary>
/// The slots and money a character's client must hear about at the end of the tick. Holds only
/// which slots were touched: the packet reads each slot's final value when it is built, so several
/// changes to one slot collapse into one entry. The bank is not sent (spec #459 section 3), so its
/// slots are never recorded.
/// </summary>
public sealed class InventoryClientChanges
{
    private readonly HashSet<(InventoryType Container, ushort Slot)> _slots = [];

    public bool MoneyChanged { get; private set; }

    public bool HasChanges => _slots.Count > 0 || MoneyChanged;

    public IReadOnlyCollection<(InventoryType Container, ushort Slot)> Slots => _slots;

    public void RecordSlot(InventoryType container, ushort slot)
    {
        if (container == InventoryType.Bank)
            return;

        _slots.Add((container, slot));
    }

    public void RecordMoney() => MoneyChanged = true;

    public void Clear()
    {
        _slots.Clear();
        MoneyChanged = false;
    }
}
