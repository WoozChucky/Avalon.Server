using Avalon.Common;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Reload;

namespace Avalon.World.Inventory;

/// <summary>
/// Turns a character's client changes into at most one SInventoryUpdatePacket. Called by
/// WorldServer once per tick per connection, after both tick passes, so everything changed during
/// the tick leaves together, each slot at its final value.
/// </summary>
public static class InventoryUpdateFlusher
{
    /// <summary>One delegate for every flush. No two entries of an update name the same slot, so none compare equal.</summary>
    private static readonly Comparison<InventorySlotUpdateDto> s_byContainerThenSlot =
        static (a, b) => Order(a) - Order(b);

    /// <param name="items">
    /// The current items generation, read once by the caller for every connection: what names a magic copy (#1179).
    /// </param>
    public static void Flush(IWorldConnection connection, ItemsPatch items)
    {
        if (connection.Character is not CharacterEntity { Data: { } row } character)
            return;

        InventoryClientChanges changes = character.ClientChanges;
        if (!changes.HasChanges)
            return;

        // Bank slots leave only while the bank is open; outside it they are dropped, so the rule
        // that Bank slots never reach a client without an open bank still holds.
        InventorySlotUpdateDto[] slots = SlotsToSend(changes, character, BankAccess.IsOpen(connection, character), items);

        ulong? gold = changes.GoldChanged ? row.Gold : null;
        changes.Clear();

        if (slots.Length == 0 && gold is null)
            return;

        connection.Send(SInventoryUpdatePacket.Create(slots, gold, PacketEncoder.Shared));
    }

    /// <summary>
    /// Each changed slot at its final value, in container and slot order, the order a client is sent them in. Only
    /// the packet's own entries are allocated (#883): the slots are counted, read into an array of that size in
    /// whatever order the set walks them, and the array is sorted in place.
    /// </summary>
    private static InventorySlotUpdateDto[] SlotsToSend(InventoryClientChanges changes, CharacterEntity character,
        bool bankOpen, ItemsPatch items)
    {
        int count = 0;
        foreach ((InventoryType container, byte _) in changes)
        {
            if (bankOpen || container != InventoryType.Bank)
                count++;
        }

        if (count == 0)
            return [];

        var slots = new InventorySlotUpdateDto[count];
        int next = 0;
        foreach ((InventoryType container, byte slot) in changes)
        {
            if (!bankOpen && container == InventoryType.Bank)
                continue;

            slots[next++] = new InventorySlotUpdateDto
            {
                Container = container,
                Slot = slot,
                Item = character.Container(container).TryGet(slot, out InventoryItem item)
                    ? ItemSlotDtoMapper.ToDto(container, item, character.ItemModifiers.Of(item.InstanceId), items)
                    : null,
            };
        }

        Array.Sort(slots, s_byContainerThenSlot);
        return slots;
    }

    /// <summary>Where an entry stands among a packet's: its container first, then its slot.</summary>
    private static int Order(InventorySlotUpdateDto entry) => ((int)entry.Container << 16) | entry.Slot;
}
