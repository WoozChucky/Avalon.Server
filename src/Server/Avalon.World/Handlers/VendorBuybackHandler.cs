using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Vendor;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Vendors;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// Buys back one of this session's last ten sales at an open shop (spec #432), on the tick, in the
/// map pass. Every request gets exactly one SMSG_VENDOR_RESULT. The exact instance returns to the
/// lowest free Bag slot in the tick's inventory update.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_VENDOR_BUYBACK)]
public class VendorBuybackHandler(ILogger<VendorBuybackHandler> logger, IWorld world, ICharacterEconomy economy)
    : WorldPacketHandler<CVendorBuybackPacket>
{
    public override void Execute(IWorldConnection connection, CVendorBuybackPacket packet)
    {
        if (connection.Character is not CharacterEntity character)
        {
            logger.LogDebug("Dropped CMSG_VENDOR_BUYBACK from a connection with no character");
            return;
        }

        VendorResult result;
        try
        {
            // Alive first, so a dead character's request neither checks the leash nor ends the conversation.
            result = character.IsDead
                ? VendorResult.Dead
                : economy.VendorOf(character).TryBuyback(
                    ShopAccess.TryUse(connection, character, world, out _), packet.Index);
        }
        catch (Exception e)
        {
            // The request still gets its one reply, and nothing escapes onto the tick. The apply order
            // takes value from the player before giving any, so a failure caused by a bug loses value,
            // never creates it, and is not rolled back.
            logger.LogError(e, "CMSG_VENDOR_BUYBACK {RequestId} for index {Index} threw; answering NotFound",
                packet.RequestId, packet.Index);
            result = VendorResult.NotFound;
        }

        connection.Send(SVendorResultPacket.Create(packet.RequestId, result, connection.CryptoSession.Encrypt));
    }
}
