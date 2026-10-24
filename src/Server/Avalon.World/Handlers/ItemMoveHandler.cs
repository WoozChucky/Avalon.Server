using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World.Auras;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public;
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
    // A refusal's modifier entries (#1179), given back once the reply is encoded. Handlers run on the tick, one at a time.
    private readonly ItemModifierDtoPool _modifierDtos = new();

    public override void Execute(IWorldConnection connection, CItemMovePacket packet)
    {
        if (connection.Character is not CharacterEntity character)
        {
            logger.LogDebug("Dropped CMSG_ITEM_MOVE from a connection with no character");
            return;
        }

        bool fromRead = SlotRef.TryParse(packet.FromContainer, packet.FromSlot, out SlotRef from);
        bool toRead = SlotRef.TryParse(packet.ToContainer, packet.ToSlot, out SlotRef to);

        // The slots a refusal reports, on the stack: a request allocates nothing to name them (#883).
        Span<SlotRef> named = stackalloc SlotRef[2];
        int readable = 0;
        if (fromRead)
            named[readable++] = from;
        if (toRead)
            named[readable++] = to;

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

        ItemRequestReply.Send(connection, character, packet.RequestId, result, named[..readable], world.Data.Items,
            _modifierDtos);
    }

    private ItemRequestResult Move(
        IWorldConnection connection, CharacterEntity character, bool readable, SlotRef from, SlotRef to, uint? count)
    {
        if (character.IsDead)
            return ItemRequestResult.Dead;

        if (!readable)
            return ItemRequestResult.InvalidSlot;

        bool touchesBank = from.Container == InventoryType.Bank || to.Container == InventoryType.Bank;
        bool touchesGear = from.Container == InventoryType.Equipment || to.Container == InventoryType.Equipment;

        // #770: a stunned character puts no gear on or off and acts on no open bank; a move within the Bag is taken.
        if ((touchesBank || touchesGear) && !ControlGates.CanAct(character))
            return ItemRequestResult.Stunned;

        bool bankAccessible = touchesBank && BankAccess.TryUse(connection, character, world);

        ItemRequestResult result = economy.InventoryOf(character).TryMove(from, to, count, bankAccessible);

        // An accepted equip or unequip ends the character's arrival protection (#1088), as one through item use does.
        if (result == ItemRequestResult.Ok && touchesGear)
        {
            CharacterStatsRefresh.AfterGearChange(character, world.Data, logger);
            ArrivalProtection.End(character);
        }

        return result;
    }
}
