using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Trade;
using Avalon.World.Public;
using Avalon.World.Trading;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>CMSG_TRADE_OFFER_ITEM (#1175): one SMSG_TRADE_RESULT.</summary>
[PacketHandler(NetworkPacketType.CMSG_TRADE_OFFER_ITEM)]
public class TradeOfferItemHandler(TradeService trades, ILogger<TradeOfferItemHandler> logger) : WorldPacketHandler<CTradeOfferItemPacket>
{
    public override void Execute(IWorldConnection connection, CTradeOfferItemPacket packet) =>
        TradeReplies.Handle(connection, logger, NetworkPacketType.CMSG_TRADE_OFFER_ITEM, (trades, packet),
            static (character, request) =>
                (request.trades.OfferItem(character.Guid.Id, request.packet.BagSlot, request.packet.TradeSlot), null));
}
