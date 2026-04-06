using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.World;
using Avalon.World.Entities;
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
    IWorld world) : WorldPacketHandler<CEnterMapPacket>
{
    public override void Execute(WorldConnection connection, CEnterMapPacket packet)
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
        // GetPortalsFrom takes ushort: chain MapTemplateId → MapId → ushort
        ushort sourceMapId = currentInstance != null
            ? (ushort)(MapId)currentInstance.TemplateId
            : (ushort)character.Map;

        IReadOnlyList<MapPortal> portals = mapManager.GetPortalsFrom(sourceMapId);
        MapPortal? portal = portals.FirstOrDefault(p => p.TargetMapId == packet.TargetMapId);

        // 5. No portal → MapNotFound
        if (portal == null)
        {
            logger.LogDebug("EnterMap: no portal from map {SourceMapId} to {TargetMapId}", sourceMapId,
                packet.TargetMapId);
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.MapNotFound,
                connection.CryptoSession.Encrypt));
            return;
        }

        // 6. Proximity check
        Vector3 portalPosition = new(portal.X, portal.Y, portal.Z);
        if (Vector3.Distance(character.Position, portalPosition) > portal.Radius)
        {
            logger.LogDebug(
                "EnterMap: character {Name} is too far from portal (distance {Distance}, radius {Radius})",
                character.Name, Vector3.Distance(character.Position, portalPosition), portal.Radius);
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
            connection.AddQueryCallback(
                world.InstanceRegistry.GetOrCreateTownInstanceAsync(targetTemplate.Id,
                    targetTemplate.MaxPlayers ?? 30),
                targetInstance => OnInstanceReceived(connection, targetInstance, targetTemplate, packet.TargetMapId));
        }
        else
        {
            connection.AddQueryCallback(
                world.InstanceRegistry.GetOrCreateNormalInstanceAsync(accountId, targetTemplate.Id),
                targetInstance => OnInstanceReceived(connection, targetInstance, targetTemplate, packet.TargetMapId));
        }
    }

    private void OnInstanceReceived(WorldConnection connection, IMapInstance targetInstance, MapTemplate targetTemplate,
        MapId targetMapId)
    {
        // 9. Transfer the player (removes from current, updates position & InstanceIdGuid, adds to target)
        world.TransferPlayer(connection, targetInstance);

        // 10. Send success response
        connection.Send(SMapTransitionPacket.Create(
            MapTransitionResult.Success,
            targetInstance.InstanceId,
            targetMapId,
            targetTemplate.DefaultSpawnX,
            targetTemplate.DefaultSpawnY,
            targetTemplate.DefaultSpawnZ,
            targetTemplate.Name,
            targetTemplate.Description,
            connection.CryptoSession.Encrypt));

        // 11. Persist updated map and position
        if (connection.Character is CharacterEntity {Data: { } dbCharacter})
        {
            dbCharacter.Map = targetMapId;
            dbCharacter.InstanceId = targetInstance.InstanceId.ToString();
            dbCharacter.X = targetTemplate.DefaultSpawnX;
            dbCharacter.Y = targetTemplate.DefaultSpawnY;
            dbCharacter.Z = targetTemplate.DefaultSpawnZ;

            connection.AddQueryCallback(characterRepository.UpdateAsync(dbCharacter), () =>
            {
                logger.LogInformation(
                    "Character {Name} transferred to map {MapId} (instance {InstanceId})",
                    connection.Character!.Name, targetMapId, targetInstance.InstanceId);
            });
        }
    }
}
