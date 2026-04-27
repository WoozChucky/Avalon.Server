using Avalon.World.Public;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_ENTER_MAP)]
public class EnterMapHandler(
    ILogger<EnterMapHandler> logger,
    IAvalonMapManager mapManager,
    ICharacterRepository characterRepository,
    IChunkLibrary chunkLibrary,
    IWorld world) : WorldPacketHandler<CEnterMapPacket>
{
    public override void Execute(IWorldConnection connection, CEnterMapPacket packet)
    {
        // 1. Guard: character must be in-game
        if (!connection.InGame)
        {
            return;
        }

        ICharacter character = connection.Character!;

        // 2. Resolve the current instance
        IMapInstance? currentInstance = world.InstanceRegistry.GetInstanceById(character.InstanceId);

        // 3. Load the target map template
        MapTemplate? targetTemplate =
            world.MapTemplates.FirstOrDefault(t => t.Id == (MapTemplateId)packet.TargetMapId);

        if (targetTemplate == null)
        {
            logger.LogDebug("EnterMap: target map {MapId} not found", packet.TargetMapId);
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.MapNotFound,
                connection.CryptoSession.Encrypt));
            return;
        }

        // 4. Find a portal on the current map that leads to the target
        bool portalFound = false;
        Vector3 portalPosition = default;
        float portalRadius = 0;

        if (currentInstance is MapInstance mi && mi.Layout is not null)
        {
            // Procedural source: iterate PortalInstance list on the instance.
            PortalInstance? match = mi.Portals.FirstOrDefault(p => p.TargetMapId == packet.TargetMapId);
            if (match is not null)
            {
                portalFound = true;
                portalPosition = match.Position;
                portalRadius = match.Radius;
            }
        }
        else
        {
            // Town source: existing DB-based MapPortal lookup.
            // GetPortalsFrom takes ushort: chain MapTemplateId → MapId → ushort
            ushort sourceMapId = currentInstance != null
                ? (ushort)(MapId)currentInstance.TemplateId
                : (ushort)character.Map;

            IReadOnlyList<MapPortal> portals = mapManager.GetPortalsFrom(sourceMapId);
            MapPortal? dbPortal = portals.FirstOrDefault(p => p.TargetMapId == packet.TargetMapId);
            if (dbPortal is not null)
            {
                portalFound = true;
                portalPosition = new Vector3(dbPortal.X, dbPortal.Y, dbPortal.Z);
                portalRadius = dbPortal.Radius;
            }
        }

        // 5. No portal → MapNotFound
        if (!portalFound)
        {
            logger.LogDebug("EnterMap: no portal to {TargetMapId}", packet.TargetMapId);
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.MapNotFound,
                connection.CryptoSession.Encrypt));
            return;
        }

        // 6. Proximity check
        if (Vector3.Distance(character.Position, portalPosition) > portalRadius)
        {
            logger.LogDebug(
                "EnterMap: character {Name} is too far from portal (distance {Distance}, radius {Radius})",
                character.Name, Vector3.Distance(character.Position, portalPosition), portalRadius);
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.NotNearPortal,
                connection.CryptoSession.Encrypt));
            return;
        }

        // 7. Level checks
        if (targetTemplate.MinLevel.HasValue && character.Level < targetTemplate.MinLevel.Value)
        {
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.LevelTooLow,
                connection.CryptoSession.Encrypt));
            return;
        }

        if (targetTemplate.MaxLevel.HasValue && character.Level > targetTemplate.MaxLevel.Value)
        {
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.LevelTooHigh,
                connection.CryptoSession.Encrypt));
            return;
        }

        // 8. Resolve target instance
        long accountId = connection.AccountId!;

        if (targetTemplate.MapType == MapType.Town)
        {
            connection.EnqueueContinuation(
                world.InstanceRegistry.GetOrCreateTownInstanceAsync(targetTemplate.Id,
                    targetTemplate.MaxPlayers ?? 30),
                targetInstance => OnInstanceReceived(connection, targetInstance, targetTemplate, packet.TargetMapId));
        }
        else
        {
            connection.EnqueueContinuation(
                world.InstanceRegistry.GetOrCreateNormalInstanceAsync(accountId, targetTemplate.Id),
                targetInstance => OnInstanceReceived(connection, targetInstance, targetTemplate, packet.TargetMapId));
        }
    }

    private void OnInstanceReceived(IWorldConnection connection, IMapInstance targetInstance, MapTemplate targetTemplate,
        MapId targetMapId)
    {
        // 9. Transfer the player (removes from current, updates position & InstanceIdGuid, adds to target)
        world.TransferPlayer(connection, targetInstance);

        // 10. Resolve spawn coords: procedural uses instance.EntrySpawnWorldPos; town uses template defaults.
        float spawnX, spawnY, spawnZ;
        if (targetTemplate.MapType == MapType.Normal
            && targetInstance is MapInstance procMiSpawn
            && procMiSpawn.EntrySpawnWorldPos is Vector3 entrySpawn)
        {
            spawnX = entrySpawn.x;
            spawnY = entrySpawn.y;
            spawnZ = entrySpawn.z;
        }
        else
        {
            spawnX = targetTemplate.DefaultSpawnX;
            spawnY = targetTemplate.DefaultSpawnY;
            spawnZ = targetTemplate.DefaultSpawnZ;
        }

        // 11. Send success response
        connection.Send(SMapTransitionPacket.Create(
            MapTransitionResult.Success,
            targetInstance.InstanceId,
            targetMapId,
            spawnX, spawnY, spawnZ,
            targetTemplate.Name,
            targetTemplate.Description,
            connection.CryptoSession.Encrypt));

        // 12. Send chunk layout packet for any instance backed by a ChunkLayout
        // (town + normal both flow through ChunkLayoutInstanceFactory now).
        if (targetInstance is MapInstance layoutMi && layoutMi.Layout is { } layout)
        {
            var dtos = layout.Chunks.Select(c => new PlacedChunkDto
            {
                ChunkTemplateId = c.TemplateId.Value,
                ChunkName = chunkLibrary.GetById(c.TemplateId).Name,
                GridX = c.GridX,
                GridZ = c.GridZ,
                Rotation = c.Rotation,
            }).ToList();
            connection.Send(SChunkLayoutPacket.Create(
                layout.Seed,
                layoutMi.InstanceId,
                layout.CellSize,
                dtos,
                layout.EntrySpawnWorldPos,
                connection.CryptoSession.Encrypt));
        }

        // 13. Persist updated map and position
        if (connection.Character is CharacterEntity {Data: { } dbCharacter})
        {
            dbCharacter.Map = targetMapId;
            dbCharacter.InstanceId = targetInstance.InstanceId.ToString();
            dbCharacter.X = spawnX;
            dbCharacter.Y = spawnY;
            dbCharacter.Z = spawnZ;

            connection.EnqueueContinuation(characterRepository.UpdateAsync(dbCharacter, CancellationToken.None), () =>
            {
                logger.LogInformation(
                    "Character {Name} transferred to map {MapId} (instance {InstanceId})",
                    connection.Character!.Name, targetMapId, targetInstance.InstanceId);
            });
        }
    }
}

