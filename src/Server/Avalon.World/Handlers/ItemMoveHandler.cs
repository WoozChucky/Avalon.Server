using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// Moves, splits, merges, swaps, equips and unequips (spec #463), on the tick, in the map pass.
/// Every request is answered with exactly one SMSG_ITEM_RESULT. An accepted change reaches the
/// client through InventoryUpdateFlusher at the end of the tick, and a stat change through the
/// ordinary state replication. Requests that race in one tick apply one after another, and the
/// second sees what the first left.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_ITEM_MOVE)]
public class ItemMoveHandler(ILogger<ItemMoveHandler> logger, IWorld world, ICharacterEconomy economy)
    : WorldPacketHandler<CItemMovePacket>
{
    public override void Execute(IWorldConnection connection, CItemMovePacket packet)
    {
        if (connection.Character is not CharacterEntity character)
        {
            logger.LogDebug("Dropped CMSG_ITEM_MOVE from a connection with no character");
            return;
        }

        bool fromRead = SlotRef.TryParse(packet.FromContainer, packet.FromSlot, out SlotRef from);
        bool toRead = SlotRef.TryParse(packet.ToContainer, packet.ToSlot, out SlotRef to);

        List<SlotRef> named = [];
        if (fromRead)
            named.Add(from);
        if (toRead)
            named.Add(to);

        ItemRequestResult result;
        try
        {
            result = Move(connection, character, fromRead && toRead, from, to, packet.Count);
        }
        catch (Exception e)
        {
            // The request still gets its one reply and nothing escapes onto the tick. Nothing is rolled
            // back: a throw before InventoryMove accepted the request changed nothing, but a throw from
            // the apply itself would leave whatever it had already written, which the tick's inventory
            // update still sends. The stats refresh cannot land here; it has its own guard.
            logger.LogError(e, "CMSG_ITEM_MOVE {RequestId} from {From} to {To} threw; answering NotFound",
                packet.RequestId, from, to);
            result = ItemRequestResult.NotFound;
        }

        ItemRequestReply.Send(connection, character, packet.RequestId, result, named);
    }

    private ItemRequestResult Move(
        IWorldConnection connection, CharacterEntity character, bool readable, SlotRef from, SlotRef to, uint? count)
    {
        if (character.IsDead)
            return ItemRequestResult.Dead;

        if (!readable)
            return ItemRequestResult.InvalidSlot;

        bool touchesBank = from.Container == InventoryType.Bank || to.Container == InventoryType.Bank;
        bool bankAccessible = touchesBank && BankAccess.TryUse(connection, character, world);

        ItemRequestResult result = economy.InventoryOf(character).TryMove(from, to, count, bankAccessible);

        if (result == ItemRequestResult.Ok
            && (from.Container == InventoryType.Equipment || to.Container == InventoryType.Equipment))
        {
            CharacterStatsRefresh.AfterGearChange(character, world.Data, logger);
        }

        return result;
    }
}
