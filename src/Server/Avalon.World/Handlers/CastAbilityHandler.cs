using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CAST_ABILITY)]
public class CastAbilityHandler(ILogger<CastAbilityHandler> logger, IWorld world)
    : WorldPacketHandler<CCastAbilityPacket>
{
    public override void Execute(IWorldConnection connection, CCastAbilityPacket packet)
    {
        var attacker = connection.Character;
        if (attacker is null || attacker.IsDead)
        {
            logger.LogDebug("Dropped CMSG_CAST_ABILITY from dead/missing char");
            return;
        }
        // E2..E6 fill the rest.
    }
}
