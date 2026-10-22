using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Trade;
using Avalon.World.Public;
using Avalon.World.Trading;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>CMSG_TRADE_REQUEST (#1175): one SMSG_TRADE_RESULT, naming the player asked when that player is online.</summary>
[PacketHandler(NetworkPacketType.CMSG_TRADE_REQUEST)]
public class TradeRequestHandler(TradeService trades, ILogger<TradeRequestHandler> logger) : WorldPacketHandler<CTradeRequestPacket>
{
    public override void Execute(IWorldConnection connection, CTradeRequestPacket packet) =>
        TradeReplies.Handle(connection, logger, NetworkPacketType.CMSG_TRADE_REQUEST, (trades, packet),
            static (character, request) =>
            {
                uint targetId = request.packet.TargetCharacterId;
                TradeResult result = request.trades.Request(character.Guid.Id, targetId);
                return (result, request.trades.Online.ById(targetId)?.Character?.Name);
            });
}
