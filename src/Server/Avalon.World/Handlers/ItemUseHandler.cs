using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Items;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// CMSG_ITEM_USE, on the tick, in the map pass: ItemUseService answers it with exactly one SMSG_ITEM_USE_RESULT.
/// A connection with no character gets nothing.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_ITEM_USE)]
public class ItemUseHandler(ILogger<ItemUseHandler> logger, ItemUseService items) : WorldPacketHandler<CItemUsePacket>
{
    public override void Execute(IWorldConnection connection, CItemUsePacket packet)
    {
        if (connection.Character is not CharacterEntity character)
        {
            logger.LogDebug("Dropped CMSG_ITEM_USE from a connection with no character");
            return;
        }

        items.Use(connection, character, packet.RequestId, packet.Container, packet.Slot);
    }
}
