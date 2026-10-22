using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Trade;
using Avalon.World.Public;
using Avalon.World.Trading;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>CMSG_TRADE_REMOVE_ITEM (#1175): one SMSG_TRADE_RESULT.</summary>
[PacketHandler(NetworkPacketType.CMSG_TRADE_REMOVE_ITEM)]
public class TradeRemoveItemHandler(TradeService trades, ILogger<TradeRemoveItemHandler> logger) : WorldPacketHandler<CTradeRemoveItemPacket>
{
    public override void Execute(IWorldConnection connection, CTradeRemoveItemPacket packet) =>
        TradeReplies.Handle(connection, logger, NetworkPacketType.CMSG_TRADE_REMOVE_ITEM, (trades, packet),
            static (character, request) => (request.trades.RemoveItem(character.Guid.Id, request.packet.TradeSlot), null));
}
