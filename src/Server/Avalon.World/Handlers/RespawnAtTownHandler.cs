using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World.ChunkLayouts;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Respawn;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_RESPAWN_AT_TOWN)]
public class RespawnAtTownHandler(
    ILogger<RespawnAtTownHandler> logger,
    IWorld world,
    IRespawnTargetResolver resolver,
    IChunkLibrary chunkLibrary) : WorldPacketHandler<CRespawnAtTownPacket>
{
    private readonly TownReturn _town = new(logger, world, resolver, chunkLibrary);

    public override void Execute(IWorldConnection connection, CRespawnAtTownPacket packet)
    {
        ICharacter? ch = connection.Character;
        if (ch is null) return;

        if (!ch.IsDead)
        {
            logger.LogDebug("Dropped CMSG_RESPAWN_AT_TOWN from non-dead char {Name}", ch.Name);
            return;
        }

        if (connection.RespawnInFlight)
        {
            logger.LogDebug("Dropped CMSG_RESPAWN_AT_TOWN — already in flight for {Name}", ch.Name);
            return;
        }

        connection.RespawnInFlight = true;
        _town.Start(connection, revive: true, dropEncounter: false);
    }
}
