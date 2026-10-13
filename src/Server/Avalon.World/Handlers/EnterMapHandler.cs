using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.World;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Respawn;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// A portal entry (<c>CMSG_ENTER_MAP</c>). The numbered steps are the ones <c>docs/instanced-maps.md</c> lists under
/// "EnterMapHandler Flow": 1 to 7 run as the packet is handled, up to the request for the target instance, and 8 to 13
/// in the connection continuation, once that instance is ready.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_ENTER_MAP)]
public partial class EnterMapHandler(
    ILogger<EnterMapHandler> logger,
    ICharacterSaver characterSaver,
    IWorld world,
    PartyService? parties = null) : WorldPacketHandler<CEnterMapPacket>
{
    public override void Execute(IWorldConnection connection, CEnterMapPacket packet)
    {
        // 1. Guards: the character must be in the game and alive, and no other move to a map may be under way.
        if (!connection.InGame)
        {
            return;
        }

        if (connection.Character is { IsDead: true } deadChar)
        {
            Log.FromDeadCharacter(logger, deadChar.Name);
            return;
        }

        ICharacter character = connection.Character!;

        // Another move to a map is under way (a scroll return, an item teleport, a respawn or a party return): it
        // decides where the character goes, so a portal entry now is refused before anything is looked up or built.
        if (connection.RespawnInFlight)
        {
            Log.MoveInProgress(logger, character.Name);
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.MoveInProgress,
                PacketEncoder.Shared));
            return;
        }

        // 2. Resolve the current instance
        IMapInstance? currentInstance = world.InstanceRegistry.GetInstanceById(character.InstanceId);

        // 3. The target map's template, by id.
        ushort targetMap = packet.TargetMapId;
        MapTemplate? targetTemplate = world.MapTemplates.FindById(targetMap);

        if (targetTemplate == null)
        {
            Log.MapNotFound(logger, targetMap);
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.MapNotFound,
                PacketEncoder.Shared));
            return;
        }

        // 4. Find a portal on the current map that leads to the target. Every instance is built from a chunk layout,
        // and its portals are the PortalInstances PortalPlacementService placed from Layout.Portals as it was built:
        // there is no other source of portals.
        if (currentInstance is not MapInstance mi)
        {
            logger.LogError("EnterMap: no live map instance for character {Name}; refusing teleport",
                character.Name);
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.MapNotFound,
                PacketEncoder.Shared));
            return;
        }

        PortalInstance? match = PortalTo(mi, targetMap);
        if (match is null)
        {
            Log.NoPortal(logger, targetMap);
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.MapNotFound,
                PacketEncoder.Shared));
            return;
        }

        // 5. Proximity check: the server's position of the character against the portal's radius.
        float distance = Vector3.Distance(character.Position, match.Position);
        if (distance > match.Radius)
        {
            Log.TooFarFromPortal(logger, character.Name, targetMap, distance, match.Radius);
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.NotNearPortal,
                PacketEncoder.Shared));
            return;
        }

        // 6. Level checks
        if (targetTemplate.MinLevel.HasValue && character.Level < targetTemplate.MinLevel.Value)
        {
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.LevelTooLow,
                PacketEncoder.Shared));
            return;
        }

        if (targetTemplate.MaxLevel.HasValue && character.Level > targetTemplate.MaxLevel.Value)
        {
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.LevelTooHigh,
                PacketEncoder.Shared));
            return;
        }

        // 7. Resolve the target instance; the rest runs once it is ready.
        ResolveTarget(connection, character, targetTemplate, targetMap, reResolved: false);
    }

    /// <summary>The first portal of <paramref name="instance" /> that leads to <paramref name="targetMap" />, or null.</summary>
    private static PortalInstance? PortalTo(MapInstance instance, ushort targetMap)
    {
        IReadOnlyList<PortalInstance> portals = instance.Portals;
        for (int i = 0; i < portals.Count; i++)
        {
            if (portals[i].TargetMapId == targetMap)
            {
                return portals[i];
            }
        }

        return null;
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
            PacketEncoder.Shared));
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
                PacketEncoder.Shared));
            return;
        }

        // Continuations run one after another on the tick and TransferPlayer adds at once, so two members
        // arriving in one tick are counted correctly.
        if (targetInstance.PlayerCount >= capacity)
        {
            connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.InstanceFull,
                PacketEncoder.Shared));
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
            Log.LeftBeforeInstanceReady(logger, character.Name);
            return;
        }

        if (ArrivesInReleasedInstance(connection, character, targetInstance, targetTemplate, targetMapId, reResolved))
        {
            return;
        }

        // 8. Exit path: drop the character from any encounter in progress on the source instance before the
        // transfer. A map transition is allowed in combat, and the encounter would be left dangling otherwise: the
        // transfer moves instance membership, not combat membership.
        IMapInstance? sourceInstance = world.InstanceRegistry.GetInstanceById(character.InstanceId);
        sourceInstance?.CombatService.DropPlayerFromEncounter(character);

        // 9. Transfer the player. World.TransferPlayer owns instance membership only: it removes the character from
        // its current instance, sets its InstanceId and adds it to the target. It does not move the character: the
        // position is the row's, written in step 13. Steps 11 and 12 must go out in this same callback: the instance
        // sends its loot snapshot on its next tick, and the client has to know the map before its drops.
        world.TransferPlayer(connection, targetInstance);

        // 10. The spawn: the entry spawn of the target instance's layout, which every MapInstance (town and normal)
        // has. The template defaults serve only a target that is not a MapInstance.
        Vector3 spawn = MapArrival.SpawnOf(targetInstance, targetTemplate);

        // 11. The success response, then 12. the instance's chunk layout, for a target that is a MapInstance: what
        // every arrival sends (MapArrival), from the layout entries the instance was built with (#883).
        MapArrival.Send(connection, targetInstance, targetTemplate, spawn);

        // 13. Persist the new map, instance and position, with any dirty inventory and money, through the one
        // save path (spec #459 section 1). The row's coordinates are the character's position.
        if (connection.Character is CharacterEntity { Data: { } dbCharacter } entity)
        {
            dbCharacter.Map = targetMapId;
            dbCharacter.InstanceId = targetInstance.InstanceId.ToString();
            dbCharacter.X = spawn.x;
            dbCharacter.Y = spawn.y;
            dbCharacter.Z = spawn.z;

            // The saver logs a failure itself; this logs the transfer once the save has finished.
            connection.EnqueueContinuation(characterSaver.Save(connection, entity), committed =>
            {
                logger.LogInformation(
                    "Character {Name} transferred to map {MapId} (instance {InstanceId}); save committed: {Committed}",
                    entity.Name, targetMapId, targetInstance.InstanceId, committed);
            });
        }
    }

    /// <summary>
    /// The lines a client's own packets can cause, as many times as it sends them (#883): each formats and allocates
    /// nothing unless Debug is written, where the <c>LogDebug</c> they replace made an array of its arguments, and
    /// boxed the numbers among them, on every call. A portal entry refused in the connection's current state is a
    /// Debug line, as a packet a session filter refuses for it is (docs/packet-handlers.md). No line holds anything the
    /// packet carries but its target map id, a number.
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Dropped CMSG_ENTER_MAP from dead char {Name}")]
        public static partial void FromDeadCharacter(ILogger logger, string name);

        [LoggerMessage(Level = LogLevel.Debug, Message = "EnterMap: {Name} is already moving to a map; refused")]
        public static partial void MoveInProgress(ILogger logger, string name);

        [LoggerMessage(Level = LogLevel.Debug, Message = "EnterMap: target map {MapId} not found")]
        public static partial void MapNotFound(ILogger logger, ushort mapId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "EnterMap: no portal to {TargetMapId}")]
        public static partial void NoPortal(ILogger logger, ushort targetMapId);

        // What is left of a diagnostic that logged this refusal at Warning, with both positions and the instance, to
        // find why a character's position stopped advancing on the server after a transition (the input sequence was
        // reset on every transfer; fixed with the diagnostic, in the same change). A client standing away from a
        // portal causes one with every request, so it is a Debug line again, with what tells one refusal from another.
        [LoggerMessage(Level = LogLevel.Debug,
            Message = "EnterMap: {Name} is too far from the portal to map {TargetMapId} (distance {Distance:F2}, radius {Radius:F2})")]
        public static partial void TooFarFromPortal(ILogger logger, string name, ushort targetMapId, float distance,
            float radius);

        [LoggerMessage(Level = LogLevel.Debug,
            Message = "EnterMap: character {Name} left the connection before its target instance was ready")]
        public static partial void LeftBeforeInstanceReady(ILogger logger, string name);
    }
}
