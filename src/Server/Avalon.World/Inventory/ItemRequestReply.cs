using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;

namespace Avalon.World.Inventory;

/// <summary>The one SMSG_ITEM_RESULT every item request gets (spec #463).</summary>
public static class ItemRequestReply
{
    /// <summary>
    /// Ok carries no slots: the change reaches the client in the tick's SInventoryUpdatePacket. A
    /// refusal carries the current absolute state of each slot the request named that exists, so a
    /// client that moved an icon early can put it back, except a Bank slot while the bank is closed.
    /// </summary>
    public static void Send(
        IWorldConnection connection,
        CharacterEntity character,
        uint requestId,
        ItemRequestResult result,
        IReadOnlyList<SlotRef> named)
    {
        InventorySlotUpdateDto[] slots = [];

        if (result != ItemRequestResult.Ok)
        {
            bool bankOpen = BankAccess.IsOpen(connection, character);

            slots = named
                .Distinct()
                .Where(slot => InventoryMove.IsUsable(character, slot))
                .Where(slot => bankOpen || slot.Container != InventoryType.Bank)
                .Select(slot => new InventorySlotUpdateDto
                {
                    Container = (ushort)slot.Container,
                    Slot = slot.Slot,
                    Item = character.Container(slot.Container).TryGet(slot.Slot, out InventoryItem item)
                        ? ItemSlotDtoMapper.ToDto(slot.Container, item)
                        : null,
                })
                .ToArray();
        }

        connection.Send(SItemResultPacket.Create(requestId, result, slots, connection.CryptoSession.Encrypt));
    }
}
