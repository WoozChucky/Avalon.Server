using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Serialization;
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
        InventorySlotUpdateDto[] slots = SlotsToSend(changes, character, BankAccess.IsOpen(connection, character));

        ulong? money = changes.MoneyChanged ? row.Money : null;
        changes.Clear();

        if (slots.Length == 0 && money is null)
            return;

        connection.Send(SInventoryUpdatePacket.Create(slots, money, PacketEncoder.Shared));
    }

    /// <summary>
    /// Each changed slot at its final value, in container and slot order. Its own method because the lambdas capture
    /// the character, and a captured variable's closure is allocated where its scope begins: in <see cref="Flush" />
    /// that was on every call, for every connection on every tick (#875), changes or not.
    /// </summary>
    private static InventorySlotUpdateDto[] SlotsToSend(InventoryClientChanges changes, CharacterEntity character,
        bool bankOpen) =>
        changes.Slots
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
}
