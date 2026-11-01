using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.World.Entities;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// Stores the player's current target on the connection. Read each tick by
/// <c>ThreatBroadcastService</c> to mirror the encounter's threat list back to the client.
/// A target is stored only when the client holds it, as of the last decision of its view (#1269):
/// any other guid clears the stored target, as a null one does, so the threat list of a unit the
/// client was never told of is never sent. The packet has no reply.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_TARGET_UNIT)]
public partial class TargetUnitHandler(ILogger<TargetUnitHandler> logger)
    : WorldPacketHandler<CTargetUnitPacket>
{
    public override void Execute(IWorldConnection connection, CTargetUnitPacket packet)
    {
        if (connection.Character is not CharacterEntity character || character.IsDead)
        {
            Log.DeadOrMissing(logger);
            return;
        }

        if (packet.TargetGuid is { } raw && !character.CharacterGameState.Knows(raw))
        {
            connection.CurrentTargetGuid = null;
            Log.NotInView(logger, raw);
            return;
        }

        connection.CurrentTargetGuid = packet.TargetGuid;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Dropped CMSG_TARGET_UNIT from dead/missing char")]
        public static partial void DeadOrMissing(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "CMSG_TARGET_UNIT named {Target}, which the client does not hold; target cleared")]
        public static partial void NotInView(ILogger logger, ulong target);
    }
}
