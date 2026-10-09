using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_ENTER_MAP)]
public class EnterMapHandler(
    ILogger<EnterMapHandler> logger,
    ICharacterSaver characterSaver,
    IChunkLibrary chunkLibrary,
    IWorld world,
    PartyService? parties = null) : WorldPacketHandler<CEnterMapPacket>
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

        // Another move to a map is under way (a scroll return, an item teleport, a respawn or a party return): it
        // decides where the character goes, so a portal entry now is refused before anything is looked up or built.
        if (connection.RespawnInFlight)
        {
            logger.LogDebug("EnterMap: {Name} is already moving to a map; refused", character.Name);
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.MoveInProgress,
                connection.CryptoSession.Encryptor));
            return;
        }

        // 2. Resolve the current instance
        IMapInstance? currentInstance = world.InstanceRegistry.GetInstanceById(character.InstanceId);

        // 3. Load the target map template
        MapTemplate? targetTemplate =
            world.MapTemplates.FirstOrDefault(t => t.Id == (MapTemplateId)packet.TargetMapId);

        if (targetTemplate == null)
        {
            logger.LogDebug("EnterMap: target map {MapId} not found", packet.TargetMapId);
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.MapNotFound,
                connection.CryptoSession.Encryptor));
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
                connection.CryptoSession.Encryptor));
            return;
        }

        PortalInstance? match = mi.Portals.FirstOrDefault(p => p.TargetMapId == packet.TargetMapId);
        if (match is null)
        {
            logger.LogDebug("EnterMap: no portal to {TargetMapId}", packet.TargetMapId);
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.MapNotFound,
                connection.CryptoSession.Encryptor));
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
                connection.CryptoSession.Encryptor));
            return;
        }

        // 7. Level checks
        if (targetTemplate.MinLevel.HasValue && character.Level < targetTemplate.MinLevel.Value)
        {
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.LevelTooLow,
                connection.CryptoSession.Encryptor));
            return;
        }

        if (targetTemplate.MaxLevel.HasValue && character.Level > targetTemplate.MaxLevel.Value)
        {
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.LevelTooHigh,
                connection.CryptoSession.Encryptor));
            return;
        }

        // 8. Resolve target instance.
        ResolveTarget(connection, character, targetTemplate, packet.TargetMapId, reResolved: false);
    }

    /// <summary>
    /// Resolves the target instance and enqueues the arrival. Procedural maps key per CHARACTER (not per account) so
    /// alts on the same account get fresh instances even within the re-entry window. Town instances are shared and
    /// don't need an owner identity. <paramref name="reResolved" /> marks the one second resolve an arrival may make
    /// when its instance was released before it got there (<see cref="ArrivesInReleasedInstance" />).
    /// </summary>
    private void ResolveTarget(IWorldConnection connection, ICharacter character, MapTemplate targetTemplate,
        MapId targetMapId, bool reResolved)
    {
        uint characterId = character.Guid.Id;

        if (targetTemplate.MapType == MapType.Town)
        {
            connection.EnqueueContinuation(
                world.InstanceRegistry.GetOrCreateTownInstanceAsync(targetTemplate.Id,
                    targetTemplate.MaxPlayers ?? 30),
                targetInstance => OnInstanceReceived(connection, character, targetInstance, targetTemplate, targetMapId,
                    reResolved));
        }
        else if (parties?.PartyOf(characterId) is { } party)
        {
            // In a party, a Normal map leads to the party's instance of it (2026-09-30); the first member through builds it.
            PartyId partyId = party.Id;
            int capacity = Math.Min(world.Configuration.MaxPartySize,
                targetTemplate.MaxPlayers is { } max ? max : world.Configuration.MaxPartySize);

            connection.EnqueueContinuation(
                world.PartyInstances.GetOrCreatePartyInstanceAsync(partyId, targetTemplate.Id),
                targetInstance => OnPartyInstanceReceived(connection, character, targetInstance, targetTemplate,
                    targetMapId, partyId, capacity, reResolved));
        }
        else
        {
            connection.EnqueueContinuation(
                world.InstanceRegistry.GetOrCreateNormalInstanceAsync(characterId, targetTemplate.Id),
                targetInstance => OnInstanceReceived(connection, character, targetInstance, targetTemplate, targetMapId,
                    reResolved));
        }
    }

    /// <summary>
    /// The resolved instance was released before the arrival reached it: an abandoned instance's lifetime
    /// (<c>Game:AbandonedInstanceLifetimeMinutes</c>) can run out between the resolve and this continuation, at 0
    /// within a tick (the last member inside a party instance left meanwhile). The character is never added to a
    /// released instance: the first time, the target is resolved again through the same path, and true is returned.
    /// A second release in a row, which nothing is known to cause, fails the move rather than resolving forever.
    /// </summary>
    private bool ArrivesInReleasedInstance(IWorldConnection connection, ICharacter character, IMapInstance targetInstance,
        MapTemplate targetTemplate, MapId targetMapId, bool reResolved)
    {
        if (world.InstanceRegistry.GetInstanceById(targetInstance.InstanceId) is not null)
            return false;

        if (!reResolved)
        {
            logger.LogInformation(
                "EnterMap: instance {InstanceId} of map {MapId} was released before {Name} arrived; resolving again",
                targetInstance.InstanceId, targetMapId, character.Name);
            ResolveTarget(connection, character, targetTemplate, targetMapId, reResolved: true);
            return true;
        }

        logger.LogWarning("EnterMap: instance {InstanceId} of map {MapId} was released again before {Name} arrived",
            targetInstance.InstanceId, targetMapId, character.Name);
        connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.MapNotFound,
            connection.CryptoSession.Encryptor));
        return true;
    }

    private void OnPartyInstanceReceived(IWorldConnection connection, ICharacter character, IMapInstance targetInstance,
        MapTemplate targetTemplate, MapId targetMapId, PartyId partyId, int capacity, bool reResolved)
    {
        if (!ReferenceEquals(connection.Character, character))
        {
            return;
        }

        // Before the membership and capacity checks: a released instance holds nobody, and the second resolve repeats both.
        if (ArrivesInReleasedInstance(connection, character, targetInstance, targetTemplate, targetMapId, reResolved))
        {
            return;
        }

        // The party can end, or the character leave it, while the instance builds: it is not let in as a member.
        if (parties?.PartyOf(character.Guid.Id)?.Id.Equals(partyId) != true)
        {
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.MapNotFound,
                connection.CryptoSession.Encryptor));
            return;
        }

        // Continuations run one after another on the tick and TransferPlayer adds at once, so two members
        // arriving in one tick are counted correctly.
        if (targetInstance.PlayerCount >= capacity)
        {
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.InstanceFull,
                connection.CryptoSession.Encryptor));
            return;
        }

        OnInstanceReceived(connection, character, targetInstance, targetTemplate, targetMapId, reResolved);
    }

    private void OnInstanceReceived(IWorldConnection connection, ICharacter character, IMapInstance targetInstance,
        MapTemplate targetTemplate, MapId targetMapId, bool reResolved)
    {
        // The character can leave the connection while the instance loads: a select of it on
        // another connection despawns it here and kicks this one. Transferring then would put a
        // discarded entity back into an instance, beside the new session's copy.
        if (!ReferenceEquals(connection.Character, character))
        {
            logger.LogDebug("EnterMap: character {Name} left the connection before its target instance was ready",
                character.Name);
            return;
        }

        if (ArrivesInReleasedInstance(connection, character, targetInstance, targetTemplate, targetMapId, reResolved))
        {
            return;
        }

        // 8b. Exit-path (Phase H): drop the character from any in-progress encounter on the
        // SOURCE instance before transferring. Combat-state gating allows in-combat map
        // transitions per spec section 7; the encounter would be left dangling otherwise
        // because TransferPlayer only manages instance membership, not combat membership.
        IMapInstance? sourceInstance = world.InstanceRegistry.GetInstanceById(character.InstanceId);
        sourceInstance?.CombatService.DropPlayerFromEncounter(character);

        // 9. Transfer the player (removes from current, updates position & InstanceIdGuid, adds to target)
        // The map transition and chunk layout must go out in this same callback: MapInstance sends
        // the loot snapshot on its next tick, and the client has to know the map before its drops.
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
            connection.CryptoSession.Encryptor));

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
                connection.CryptoSession.Encryptor));
        }

        // 13. Persist updated map and position, with any dirty inventory and money, through the one
        // save path (spec #459 section 1).
        if (connection.Character is CharacterEntity { Data: { } dbCharacter } entity)
        {
            dbCharacter.Map = targetMapId;
            dbCharacter.InstanceId = targetInstance.InstanceId.ToString();
            dbCharacter.X = spawnX;
            dbCharacter.Y = spawnY;
            dbCharacter.Z = spawnZ;

            // The saver logs a failure itself; this logs the transfer once the save has finished.
            connection.EnqueueContinuation(characterSaver.Save(connection, entity), committed =>
            {
                logger.LogInformation(
                    "Character {Name} transferred to map {MapId} (instance {InstanceId}); save committed: {Committed}",
                    entity.Name, targetMapId, targetInstance.InstanceId, committed);
            });
        }
    }
}

