using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;

namespace Avalon.World.Inventory;

/// <summary>
/// Turns a character's client changes into at most one SInventoryUpdatePacket. Called by
/// WorldServer once per tick per connection, after both tick passes, so everything changed during
/// the tick leaves together, each slot at its final value.
/// </summary>
public static class InventoryUpdateFlusher
{
    public static void Flush(IWorldConnection connection)
    {
        if (connection.Character is not CharacterEntity { Data: { } row } character)
            return;

        InventoryClientChanges changes = character.ClientChanges;
        if (!changes.HasChanges)
            return;

        // Bank slots leave only while the bank is open; outside it they are dropped, so the rule
        // that Bank slots never reach a client without an open bank still holds.
        bool bankOpen = BankAccess.IsOpen(connection, character);

        InventorySlotUpdateDto[] slots = changes.Slots
            .Where(s => bankOpen || s.Container != InventoryType.Bank)
            .OrderBy(s => s.Container)
            .ThenBy(s => s.Slot)
            .Select(s => new InventorySlotUpdateDto
            {
                Container = (ushort)s.Container,
                Slot = s.Slot,
                Item = character.Container(s.Container).TryGet(s.Slot, out InventoryItem item)
                    ? ItemSlotDtoMapper.ToDto(s.Container, item)
                    : null,
            })
            .ToArray();

        ulong? money = changes.MoneyChanged ? row.Money : null;
        changes.Clear();

        if (slots.Length == 0 && money is null)
            return;

        connection.Send(SInventoryUpdatePacket.Create(slots, money, connection.CryptoSession.Encrypt));
    }
}
