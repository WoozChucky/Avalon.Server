using Avalon.Common;

namespace Avalon.World.Inventory;

/// <summary>One slot of one of a character's containers. Requests address slots, never item ids.</summary>
public readonly record struct SlotRef(InventoryType Container, byte Slot)
{
    /// <summary>
    /// Reads a slot off the wire. False for a container that is not an InventoryType (a number off
    /// the wire need not be a member of the enum its field is typed with); whether the slot exists
    /// in its container is InventoryMove's question, not this one. A slot number past 255 never
    /// gets here: it does not fit the packet's byte and fails the decode.
    /// </summary>
    public static bool TryParse(InventoryType container, byte slot, out SlotRef parsed)
    {
        if (container > InventoryType.Bank)
        {
            parsed = default;
            return false;
        }

        parsed = new SlotRef(container, slot);
        return true;
    }

    public override string ToString() => $"{Container}[{Slot}]";
}
