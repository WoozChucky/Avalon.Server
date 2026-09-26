using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Vendor;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Quests;
using Avalon.World.Vendors;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// Buys from an open shop (spec #432), on the tick, in the map pass. Every request gets exactly one
/// SMSG_VENDOR_RESULT. The gold and the item reach the client through InventoryUpdateFlusher at
/// the end of the tick, and a stock change reaches every open shop through the instance's vendor
/// pass. Two buys of the last unit in one tick apply one after the other, and the second sees
/// what the first left. The sale is timed by the container's TimeProvider, the clock the vendor
/// pass restocks by.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_VENDOR_BUY)]
public class VendorBuyHandler(
    ILogger<VendorBuyHandler> logger,
    IWorld world,
    ICharacterEconomy economy,
    IQuestProgress quests,
    TimeProvider time) : WorldPacketHandler<CVendorBuyPacket>
{
    public override void Execute(IWorldConnection connection, CVendorBuyPacket packet)
    {
        if (connection.Character is not CharacterEntity character)
        {
            logger.LogDebug("Dropped CMSG_VENDOR_BUY from a connection with no character");
            return;
        }

        VendorResult result;
        try
        {
            // Alive first, so a dead character's request neither checks the leash nor ends the conversation.
            if (character.IsDead)
            {
                result = VendorResult.Dead;
            }
            else
            {
                bool open = ShopAccess.TryUse(connection, character, world, out VendorStockState? stock);
                result = economy.VendorOf(character).TryBuy(
                    open, stock, packet.Sequence, packet.Count, quests, time.GetUtcNow().UtcDateTime);
            }
        }
        catch (Exception e)
        {
            // The request still gets its one reply, and nothing escapes onto the tick.
            logger.LogError(e, "CMSG_VENDOR_BUY {RequestId} for row {Sequence} threw; answering NotFound",
                packet.RequestId, packet.Sequence);
            result = VendorResult.NotFound;
        }

        connection.Send(SVendorResultPacket.Create(packet.RequestId, result, connection.CryptoSession.Encrypt));
    }
}
