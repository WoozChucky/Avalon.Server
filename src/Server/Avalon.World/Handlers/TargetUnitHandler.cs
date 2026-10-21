using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// Stores the player's current target on the connection. Read each tick by
/// <c>ThreatBroadcastService</c> to mirror the encounter's threat list back to the client.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_TARGET_UNIT)]
public class TargetUnitHandler(ILogger<TargetUnitHandler> logger)
    : WorldPacketHandler<CTargetUnitPacket>
{
    public override void Execute(IWorldConnection connection, CTargetUnitPacket packet)
    {
        if (connection.Character is null || connection.Character.IsDead)
        {
            logger.LogDebug("Dropped CMSG_TARGET_UNIT from dead/missing char");
            return;
        }

        connection.CurrentTargetGuid = packet.TargetGuid;
    }
}
