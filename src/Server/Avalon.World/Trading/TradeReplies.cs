using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Trade;
using Avalon.World.Entities;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Trading;

/// <summary>The one SMSG_TRADE_RESULT a trade request is answered with (#1175).</summary>
public static class TradeReplies
{
    /// <summary>
    /// Runs one trade request for the connection's character and answers it exactly once, after anything the request
    /// sent itself (the target's request, the opened trade, its state). A connection with no character is not
    /// answered. A request that throws is logged and answered <see cref="TradeResult.Error" />; a throw from sending the
    /// answer itself is left to the caller, so nothing is answered twice.
    /// </summary>
    /// <remarks>
    /// What the request needs (the trade service, the packet) is handed over as <paramref name="state" /> and back to
    /// <paramref name="handle" />, which is written <c>static</c>, as the party requests' are (#883): a lambda that
    /// captured them would cost every trade packet a closure and a delegate. A value tuple is the usual state, and is
    /// never boxed.
    /// </remarks>
    public static void Handle<TState>(IWorldConnection connection, ILogger logger, NetworkPacketType request, TState state,
        Func<CharacterEntity, TState, (TradeResult Result, string? Name)> handle)
    {
        if (connection.Character is not CharacterEntity character)
            return;

        TradeResult result;
        string? name;
        try
        {
            (result, name) = handle(character, state);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Trade request {Request} from character {CharacterId} failed", request, character.Guid.Id);
            (result, name) = (TradeResult.Error, null);
        }

        connection.Send(STradeResultPacket.Create(result, name, PacketEncoder.Shared));
    }
}
