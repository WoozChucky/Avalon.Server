using Avalon.Common.GameAuth;
using Avalon.Network.Packets.Generic;

namespace Avalon.World.GameAuth;

/// <summary>What a world tells identity about a session's end (#967), from why it closed the connection, if it did.</summary>
public static class SessionEndReasons
{
    public static string For(DisconnectReason? reason) => reason switch
    {
        DisconnectReason.ServerShutdown => GameSessionEndReasons.Shutdown,
        DisconnectReason.Maintenance => GameSessionEndReasons.Maintenance,
        DisconnectReason.Kicked or DisconnectReason.Banned or DisconnectReason.Deactivated => GameSessionEndReasons.Kicked,
        // The peer closed, the connection was lost or went silent, a slow connection, a failed save, a takeover (whose end
        // changes nothing), and SelectTimeout: a select the world could not finish (stalled, or busy), never the player's
        // idling. Each holds the place for the grace.
        _ => GameSessionEndReasons.Drop,
    };
}
