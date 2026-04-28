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
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_ENTER_MAP)]
public class EnterMapHandler(
    ILogger<EnterMapHandler> logger,
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

        if (connection.Character is { IsDead: true } deadChar)
        {
            logger.LogDebug("Dropped CMSG_ENTER_MAP from dead char {Name}", deadChar.Name);
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

        // 4. Find a portal on the current map that leads to the target.
        // Every instance is now chunk-layout-built and carries its portals on Layout.Portals
        // (surfaced as PortalInstance via PortalPlacementService). The legacy DB MapPortal
        // fallback was deleted with this task.
        if (currentInstance is not MapInstance mi || mi.Layout is null)
        {
            logger.LogError("EnterMap: current instance has no Layout; refusing teleport for character {Name}",
                character.Name);
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.MapNotFound,
                connection.CryptoSession.Encrypt));
            return;
        }

        PortalInstance? match = mi.Portals.FirstOrDefault(p => p.TargetMapId == packet.TargetMapId);
        if (match is null)
        {
            logger.LogDebug("EnterMap: no portal to {TargetMapId}", packet.TargetMapId);
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.MapNotFound,
                connection.CryptoSession.Encrypt));
            return;
        }

        Vector3 portalPosition = match.Position;
        float portalRadius = match.Radius;

        // 6. Proximity check
        if (Vector3.Distance(character.Position, portalPosition) > portalRadius)
        {
            // TEMP DIAG: include character + portal world coords + current instance id so we can
            // correlate stale-state cases (player position not advancing on server post-transition).
            logger.LogWarning(
                "EnterMap: {Name} too far from portal — charPos={CharPos} portalPos={PortalPos} distance={Distance:F2} radius={Radius:F2} charInstance={CharInstance} targetMap={TargetMap}",
                character.Name, character.Position, portalPosition,
                Vector3.Distance(character.Position, portalPosition), portalRadius,
                character.InstanceId, packet.TargetMapId);
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

        // 8. Resolve target instance. Procedural maps key per CHARACTER (not per account) so
        // alts on the same account get fresh instances even within the 15-min re-entry window.
        // Town instances are shared and don't need an owner identity.
        uint characterId = character.Guid.Id;

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
                world.InstanceRegistry.GetOrCreateNormalInstanceAsync(characterId, targetTemplate.Id),
                targetInstance => OnInstanceReceived(connection, targetInstance, targetTemplate, packet.TargetMapId));
        }
    }

    private void OnInstanceReceived(IWorldConnection connection, IMapInstance targetInstance, MapTemplate targetTemplate,
        MapId targetMapId)
    {
        // 9. Transfer the player (removes from current, updates position & InstanceIdGuid, adds to target)
        world.TransferPlayer(connection, targetInstance);

        // 10. Resolve spawn coords: every chunk-layout-built instance (town + normal) carries
        // the canonical entry spawn on its Layout. Fall back to template defaults only if the
        // instance somehow lacks one (defensive — should not happen post-Task 7).
        float spawnX, spawnY, spawnZ;
        if (targetInstance is MapInstance miSpawn && miSpawn.EntrySpawnWorldPos is Vector3 entrySpawn)
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
            var portalDtos = layout.Portals.Select(p => new PortalPlacementDto
            {
                Role = (byte)p.Role,
                WorldPos = Vector3Dto.From(p.WorldPos),
                Radius = p.Radius,
                TargetMapId = p.TargetMapId,
            }).ToList();
            connection.Send(SChunkLayoutPacket.Create(
                layout.Seed,
                layoutMi.InstanceId,
                targetMapId.Value,
                layout.CellSize,
                dtos,
                layout.EntrySpawnWorldPos,
                portalDtos,
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

