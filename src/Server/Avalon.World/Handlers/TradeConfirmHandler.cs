using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Trade;
using Avalon.World.Public;
using Avalon.World.Trading;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>CMSG_TRADE_CONFIRM (#1175): one SMSG_TRADE_RESULT.</summary>
[PacketHandler(NetworkPacketType.CMSG_TRADE_CONFIRM)]
public class TradeConfirmHandler(TradeService trades, ILogger<TradeConfirmHandler> logger) : WorldPacketHandler<CTradeConfirmPacket>
{
    public override void Execute(IWorldConnection connection, CTradeConfirmPacket packet) =>
        TradeReplies.Handle(connection, logger, NetworkPacketType.CMSG_TRADE_CONFIRM, (trades, packet),
            static (character, request) => (request.trades.Confirm(character.Guid.Id, request.packet.Revision), null));
}
