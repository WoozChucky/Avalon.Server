using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Trade;
using Avalon.World.Public;
using Avalon.World.Trading;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>CMSG_TRADE_SET_GOLD (#1175): one SMSG_TRADE_RESULT.</summary>
[PacketHandler(NetworkPacketType.CMSG_TRADE_SET_GOLD)]
public class TradeSetGoldHandler(TradeService trades, ILogger<TradeSetGoldHandler> logger) : WorldPacketHandler<CTradeSetGoldPacket>
{
    public override void Execute(IWorldConnection connection, CTradeSetGoldPacket packet) =>
        TradeReplies.Handle(connection, logger, NetworkPacketType.CMSG_TRADE_SET_GOLD, (trades, packet),
            static (character, request) => (request.trades.SetGold(character.Guid.Id, request.packet.Gold), null));
}
