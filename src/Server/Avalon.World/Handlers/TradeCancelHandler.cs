using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Trade;
using Avalon.World.Public;
using Avalon.World.Trading;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>CMSG_TRADE_CANCEL (#1175): one SMSG_TRADE_RESULT.</summary>
[PacketHandler(NetworkPacketType.CMSG_TRADE_CANCEL)]
public class TradeCancelHandler(TradeService trades, ILogger<TradeCancelHandler> logger) : WorldPacketHandler<CTradeCancelPacket>
{
    public override void Execute(IWorldConnection connection, CTradeCancelPacket packet) =>
        TradeReplies.Handle(connection, logger, NetworkPacketType.CMSG_TRADE_CANCEL, (trades, packet),
            static (character, request) => (request.trades.Cancel(character.Guid.Id), null));
}
