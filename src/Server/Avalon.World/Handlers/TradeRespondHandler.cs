using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Trade;
using Avalon.World.Public;
using Avalon.World.Trading;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>CMSG_TRADE_RESPOND (#1175): one SMSG_TRADE_RESULT.</summary>
[PacketHandler(NetworkPacketType.CMSG_TRADE_RESPOND)]
public class TradeRespondHandler(TradeService trades, ILogger<TradeRespondHandler> logger) : WorldPacketHandler<CTradeRespondPacket>
{
    public override void Execute(IWorldConnection connection, CTradeRespondPacket packet) =>
        TradeReplies.Handle(connection, logger, NetworkPacketType.CMSG_TRADE_RESPOND, (trades, packet),
            static (character, request) => (request.trades.Respond(character.Guid.Id, request.packet.Accept), null));
}
