using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Vendor;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Vendors;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// Sells from one Bag slot to an open shop (spec #432), on the tick, in the map pass. Every request
/// gets exactly one SMSG_VENDOR_RESULT. The gold and the emptied slot leave in the tick's inventory
/// update, and the sale joins the seller's buyback list in the next vendor pass.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_VENDOR_SELL)]
public class VendorSellHandler(ILogger<VendorSellHandler> logger, IWorld world, ICharacterEconomy economy)
    : WorldPacketHandler<CVendorSellPacket>
{
    public override void Execute(IWorldConnection connection, CVendorSellPacket packet)
    {
        if (connection.Character is not CharacterEntity character)
        {
            logger.LogDebug("Dropped CMSG_VENDOR_SELL from a connection with no character");
            return;
        }

        VendorResult result;
        try
        {
            // Alive first, so a dead character's request neither checks the leash nor ends the conversation.
            result = character.IsDead
                ? VendorResult.Dead
                : economy.VendorOf(character).TrySell(
                    ShopAccess.TryUse(connection, character, world, out _), packet.BagSlot, packet.Count);
        }
        catch (Exception e)
        {
            // The request still gets its one reply, and nothing escapes onto the tick. The apply order
            // takes value from the player before giving any, so a failure caused by a bug loses value,
            // never creates it, and is not rolled back.
            logger.LogError(e, "CMSG_VENDOR_SELL {RequestId} from bag slot {Slot} threw; answering NotFound",
                packet.RequestId, packet.BagSlot);
            result = VendorResult.NotFound;
        }

        connection.Send(SVendorResultPacket.Create(packet.RequestId, result, connection.CryptoSession.Encrypt));
    }
}
