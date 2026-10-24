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
    /// <param name="pool">
    /// The flush's modifier entries (#1179), owned by the caller on the tick: an update's are taken from it and given back
    /// once the packet is encoded, so a flush makes no modifier garbage once the pool holds enough.
    /// </param>
    public static void Flush(IWorldConnection connection, ItemsPatch items, ItemModifierDtoPool pool)
    {
        if (connection.Character is not CharacterEntity { Data: { } row } character)
            return;

        InventoryClientChanges changes = character.ClientChanges;
        if (!changes.HasChanges)
            return;

        // Bank slots leave only while the bank is open; outside it they are dropped, so the rule
        // that Bank slots never reach a client without an open bank still holds.
        InventorySlotUpdateDto[] slots = SlotsToSend(changes, character, BankAccess.IsOpen(connection, character), items, pool);

        ulong? gold = changes.GoldChanged ? row.Gold : null;
        changes.Clear();

        if (slots.Length == 0 && gold is null)
            return;

        connection.Send(SInventoryUpdatePacket.Create(slots, gold, PacketEncoder.Shared));
        // Create encoded the entries: their modifier lists go back for the next flush.
        pool.Return(slots);
    }

    /// <summary>
    /// Each changed slot at its final value, in container and slot order, the order a client is sent them in. Only
    /// the packet's own entries are allocated (#883): the slots are counted, read into an array of that size in
    /// whatever order the set walks them, and the array is sorted in place.
    /// </summary>
    private static InventorySlotUpdateDto[] SlotsToSend(InventoryClientChanges changes, CharacterEntity character,
        bool bankOpen, ItemsPatch items, ItemModifierDtoPool pool)
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
                    ? ItemSlotDtoMapper.ToDto(container, item, character.ItemModifiers, items, pool)
                    : null,
            };
        }

        Array.Sort(slots, s_byContainerThenSlot);
        return slots;
    }

    /// <summary>Where an entry stands among a packet's: its container first, then its slot.</summary>
    private static int Order(InventorySlotUpdateDto entry) => ((int)entry.Container << 16) | entry.Slot;
}
