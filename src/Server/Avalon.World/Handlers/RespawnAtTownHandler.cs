using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.World;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Respawn;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_RESPAWN_AT_TOWN)]
public class RespawnAtTownHandler(
    ILogger<RespawnAtTownHandler> logger,
    IWorld world,
    IRespawnTargetResolver resolver) : WorldPacketHandler<CRespawnAtTownPacket>
{
    public override void Execute(IWorldConnection connection, CRespawnAtTownPacket packet)
    {
        var ch = connection.Character;
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

        var mapTemplateId = new MapTemplateId(ch.Map.Value);

        connection.EnqueueContinuation(
            resolver.ResolveTownAsync(mapTemplateId, CancellationToken.None),
            townMapId => OnTownResolved(connection, ch, townMapId));
    }

    private void OnTownResolved(IWorldConnection connection, ICharacter ch, MapTemplateId townMapId)
    {
        var maxPlayers = world.MapTemplates.FirstOrDefault(t => t.Id == townMapId)?.MaxPlayers ?? 30;

        connection.EnqueueContinuation(
            world.InstanceRegistry.GetOrCreateTownInstanceAsync(townMapId, (ushort)maxPlayers),
            townInstance => OnInstanceReady(connection, ch, townMapId, townInstance));
    }

    private void OnInstanceReady(IWorldConnection connection, ICharacter ch, MapTemplateId townMapId, IMapInstance townInstance)
    {
        // Transfer first so MapInstance.AddCharacter is the boundary that enables broadcast.
        world.TransferPlayer(connection, townInstance);

        // Resolve spawn coords from the town's chunk layout. Fall back to template defaults defensively.
        float spawnX, spawnY, spawnZ;
        var townTemplate = world.MapTemplates.First(t => t.Id == townMapId);
        if (townInstance is MapInstance mi && mi.EntrySpawnWorldPos is { } s)
        {
            spawnX = s.x; spawnY = s.y; spawnZ = s.z;
        }
        else
        {
            spawnX = townTemplate.DefaultSpawnX;
            spawnY = townTemplate.DefaultSpawnY;
            spawnZ = townTemplate.DefaultSpawnZ;
        }

        ch.Position = new Vector3(spawnX, spawnY, spawnZ);

        // Revive() atomically clears IsDead and restores HP. Both fields dirty in a single
        // method so the next broadcast tick emits "alive + full HP" together.
        ch.Revive();

        // Clear the in-flight flag so the player can die + respawn again on a future engagement.
        connection.RespawnInFlight = false;

        // Send the standard transition packet the client already handles.
        connection.Send(SMapTransitionPacket.Create(
            MapTransitionResult.Success,
            townInstance.InstanceId,
            townMapId,
            spawnX, spawnY, spawnZ,
            townTemplate.Name,
            townTemplate.Description,
            connection.CryptoSession.Encrypt));

        logger.LogInformation("Character {Name} respawned at town {Map} instance {Instance}",
            ch.Name, townMapId.Value, townInstance.InstanceId);
    }
}
