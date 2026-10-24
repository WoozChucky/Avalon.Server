using Avalon.Common;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Reload;

namespace Avalon.World.Inventory;

/// <summary>The one SMSG_ITEM_RESULT every item request gets (spec #463).</summary>
public static class ItemRequestReply
{
    /// <summary>
    /// Ok carries no slots: the change reaches the client in the tick's SInventoryUpdatePacket. A
    /// refusal carries the current absolute state of each slot the request named that exists, so a
    /// client that moved an icon early can put it back, except a Bank slot while the bank is closed.
    /// </summary>
    /// <param name="named">
    /// The slots the request named that could be read, in the request's order. A span, so a handler names them
    /// from its own stack and a request costs no list (#883).
    /// </param>
    /// <param name="items">The current items generation, which names a magic copy a refusal reports (#1179).</param>
    /// <param name="pool">
    /// The handler's modifier entries (#1179): a refusal's are taken from it and given back once the reply is encoded.
    /// </param>
    public static void Send(
        IWorldConnection connection,
        CharacterEntity character,
        uint requestId,
        ItemRequestResult result,
        ReadOnlySpan<SlotRef> named,
        ItemsPatch items,
        ItemModifierDtoPool pool)
    {
        InventorySlotUpdateDto[] slots =
            result == ItemRequestResult.Ok ? [] : CurrentState(connection, character, named, items, pool);

        connection.Send(SItemResultPacket.Create(requestId, result, slots, PacketEncoder.Shared));
        pool.Return(slots);
    }

    /// <summary>
    /// The entries of a refusal, in the order the request named their slots. Only they are allocated: the slots
    /// are counted first, then read into an array of that size.
    /// </summary>
    private static InventorySlotUpdateDto[] CurrentState(
        IWorldConnection connection, CharacterEntity character, ReadOnlySpan<SlotRef> named, ItemsPatch items,
        ItemModifierDtoPool pool)
    {
        bool bankOpen = BankAccess.IsOpen(connection, character);

        int count = 0;
        for (int i = 0; i < named.Length; i++)
        {
            if (IsSent(character, named, i, bankOpen))
                count++;
        }

        if (count == 0)
            return [];

        var slots = new InventorySlotUpdateDto[count];
        int next = 0;
        for (int i = 0; i < named.Length; i++)
        {
            if (!IsSent(character, named, i, bankOpen))
                continue;

            SlotRef slot = named[i];
            slots[next++] = new InventorySlotUpdateDto
            {
                Container = slot.Container,
                Slot = slot.Slot,
                Item = character.Container(slot.Container).TryGet(slot.Slot, out InventoryItem item)
                    ? ItemSlotDtoMapper.ToDto(slot.Container, item, character.ItemModifiers, items, pool)
                    : null,
            };
        }

        return slots;
    }

    /// <summary>
    /// Whether the slot named at <paramref name="index" /> gets an entry: it was not already named before it (a
    /// slot named twice is sent once, where it was first named), it exists, and it is not a Bank slot while the
    /// bank is closed.
    /// </summary>
    private static bool IsSent(CharacterEntity character, ReadOnlySpan<SlotRef> named, int index, bool bankOpen)
    {
        SlotRef slot = named[index];

        return !named[..index].Contains(slot)
               && InventoryMove.IsUsable(character, slot)
               && (bankOpen || slot.Container != InventoryType.Bank);
    }
}
