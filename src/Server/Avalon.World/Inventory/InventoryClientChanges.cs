using Avalon.World.Public.Enums;

namespace Avalon.World.Inventory;

/// <summary>
/// The slots and money a character's client must hear about at the end of the tick. Holds only
/// which slots were touched: the packet reads each slot's final value when it is built, so several
/// changes to one slot collapse into one entry. Bank slots are recorded too; InventoryUpdateFlusher
/// sends them only while the bank is open (spec #463) and drops them otherwise.
/// </summary>
public sealed class InventoryClientChanges
{
    private readonly HashSet<(InventoryType Container, ushort Slot)> _slots = [];

    public bool MoneyChanged { get; private set; }

    public bool HasChanges => _slots.Count > 0 || MoneyChanged;

    public IReadOnlyCollection<(InventoryType Container, ushort Slot)> Slots => _slots;

    public void RecordSlot(InventoryType container, ushort slot) => _slots.Add((container, slot));

    public void RecordMoney() => MoneyChanged = true;

    public void Clear()
    {
        _slots.Clear();
        MoneyChanged = false;
    }
}
