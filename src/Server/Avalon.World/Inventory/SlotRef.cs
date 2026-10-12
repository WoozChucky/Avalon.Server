using Avalon.Common;

namespace Avalon.World.Inventory;

/// <summary>One slot of one of a character's containers. Requests address slots, never item ids.</summary>
public readonly record struct SlotRef(InventoryType Container, ushort Slot)
{
    /// <summary>
    /// Reads a slot off the wire. False for a container that is not an InventoryType (a number off
    /// the wire need not be a member of the enum its field is typed with) or a slot number too large
    /// for any container; whether the slot exists in its container is InventoryMove's question, not
    /// this one.
    /// </summary>
    public static bool TryParse(InventoryType container, uint slot, out SlotRef parsed)
    {
        if (container > InventoryType.Bank || slot > ushort.MaxValue)
        {
            parsed = default;
            return false;
        }

        parsed = new SlotRef(container, (ushort)slot);
        return true;
    }

    public override string ToString() => $"{Container}[{Slot}]";
}
