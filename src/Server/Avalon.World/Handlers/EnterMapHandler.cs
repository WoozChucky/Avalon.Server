using Avalon.Common.Mathematics;
using Avalon.Common.Telemetry;
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
/// <remarks>
/// From the moment a request is accepted (step 7) until it ends, the character is moving to a map: the connection's
/// <c>RespawnInFlight</c> is set, as it is during a return to town or a teleport, so a second portal entry, and any
/// other move, is refused until this one ends. Every ending clears it (<see cref="Arrive" />): the arrival, a
/// refusal on arrival (a character that died meanwhile among them), an instance that could not be built, and
/// anything that throws on the way. The one ending
/// that leaves it alone is the character having left the connection meanwhile: the connection cleared it as the
/// character left, and it may already be the next character's.
/// </remarks>
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
            Count(Refusal.Dead);
            return;
        }

        ICharacter character = connection.Character!;

        // Another move to a map is under way (a scroll return, an item teleport, a respawn or a party return): it
        // decides where the character goes, so a portal entry now is refused before anything is looked up or built.
        if (connection.RespawnInFlight)
        {
            Log.MoveInProgress(logger, character.Name);
            Refuse(connection, MapTransitionResult.MoveInProgress, Refusal.MoveInProgress);
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
            Refuse(connection, MapTransitionResult.MapNotFound, Refusal.UnknownMap);
            return;
        }

        // 4. Find a portal on the current map that leads to the target. Every instance is built from a chunk layout,
        // and its portals are the PortalInstances PortalPlacementService placed from Layout.Portals as it was built:
        // there is no other source of portals.
        if (currentInstance is not MapInstance mi)
        {
            logger.LogError("EnterMap: no live map instance for character {Name}; refusing teleport",
                character.Name);
            Refuse(connection, MapTransitionResult.MapNotFound, Refusal.NoInstance);
            return;
        }

        PortalInstance? match = PortalTo(mi, targetMap);
        if (match is null)
        {
            Log.NoPortal(logger, targetMap);
            Refuse(connection, MapTransitionResult.MapNotFound, Refusal.NoPortal);
            return;
        }

        // 5. Proximity check: the server's position of the character against the portal's radius.
        float distance = Vector3.Distance(character.Position, match.Position);
        if (distance > match.Radius)
        {
            Log.TooFarFromPortal(logger, character.Name, targetMap, distance, match.Radius);
            Refuse(connection, MapTransitionResult.NotNearPortal, Refusal.NotNearPortal);
            return;
        }

        // 6. Level checks
        if (targetTemplate.MinLevel.HasValue && character.Level < targetTemplate.MinLevel.Value)
        {
            Refuse(connection, MapTransitionResult.LevelTooLow, Refusal.LevelTooLow);
            return;
        }

        if (targetTemplate.MaxLevel.HasValue && character.Level > targetTemplate.MaxLevel.Value)
        {
            Refuse(connection, MapTransitionResult.LevelTooHigh, Refusal.LevelTooHigh);
            return;
        }

        // 7. Resolve the target instance; the rest runs once it is ready. The request is accepted, so the move is
        // under way from here: a second portal entry in this same pass, before the first one's continuation has run,
        // is refused in step 1 rather than arriving and saving once more.
        connection.RespawnInFlight = true;
        try
        {
            ResolveTarget(connection, character, targetTemplate, targetMap, reResolved: false);
        }
        catch
        {
            // Nothing was queued, so nothing would ever end the move.
            connection.RespawnInFlight = false;
            throw;
        }
    }

    /// <summary>Answers the request with <paramref name="result" /> and counts it under <paramref name="reason" />.</summary>
    private static void Refuse(IWorldConnection connection, MapTransitionResult result, string reason)
    {
        Count(reason);
        connection.Send(SMapTransitionPacket.CreateFailure(result, PacketEncoder.Shared));
    }

    /// <summary>
    /// One more refused portal entry in <c>world.portal_entry.refusals</c>, under <paramref name="reason" />: one tag,
    /// passed as a struct, so a refusal still allocates nothing.
    /// </summary>
    private static void Count(string reason) =>
        DiagnosticsConfig.World.PortalEntryRefusals.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>
    /// The <c>reason</c> a refused portal entry is counted under: a fixed set, one per place the handler refuses,
    /// since <c>MapNotFound</c> alone answers five of them. Since the refusals log at Debug, the count is what shows
    /// them at the level a world ships with: a rate of <see cref="NotNearPortal" /> is clients and the server
    /// disagreeing about where characters stand, or a client asking for portals it is not at.
    /// </summary>
    private static class Refusal
    {
        /// <summary>From a dead character, or one that died before its instance was ready: dropped, with no reply.</summary>
        public const string Dead = "dead";

        /// <summary>Another move, or an earlier portal entry, is under way (<c>MoveInProgress</c>).</summary>
        public const string MoveInProgress = "move_in_progress";

        /// <summary>No map has the id asked for (<c>MapNotFound</c>).</summary>
        public const string UnknownMap = "unknown_map";

        /// <summary>The character is in no live instance (<c>MapNotFound</c>): the server's own fault, logged at Error.</summary>
        public const string NoInstance = "no_instance";

        /// <summary>The map the character is on has no portal to the one asked for (<c>MapNotFound</c>).</summary>
        public const string NoPortal = "no_portal";

        /// <summary>The character is outside the portal's radius, by the server's position of it (<c>NotNearPortal</c>).</summary>
        public const string NotNearPortal = "not_near_portal";

        public const string LevelTooLow = "level_too_low";
        public const string LevelTooHigh = "level_too_high";

        /// <summary>On arrival: the instance was released before the arrival, twice in a row (<c>MapNotFound</c>).</summary>
        public const string InstanceReleased = "instance_released";

        /// <summary>On arrival: no longer in the party the instance is for (<c>MapNotFound</c>).</summary>
        public const string NotInParty = "not_in_party";

        /// <summary>On arrival: the party's instance is full (<c>InstanceFull</c>).</summary>
        public const string InstanceFull = "instance_full";
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
    /// alts on the same account get fresh instances even within the re-entry window; in a party, a Normal map leads to
    /// the party's instance of it (2026-09-30), which the first member through builds. Town instances are shared and
    /// don't need an owner identity. <paramref name="reResolved" /> marks the one second resolve an arrival may make
    /// when its instance was released before it got there (<see cref="ArrivesInReleasedInstance" />).
    /// </summary>
    private void ResolveTarget(IWorldConnection connection, ICharacter character, MapTemplate targetTemplate,
        MapId targetMapId, bool reResolved)
    {
        uint characterId = character.Guid.Id;

        // One continuation per branch, each capturing only what its branch has: an arrival outside a party allocates
        // the closure it always did, with nothing of a party's in it.
        if (targetTemplate.MapType == MapType.Town)
        {
            connection.EnqueueContinuation(
                Settled(world.InstanceRegistry.GetOrCreateTownInstanceAsync(targetTemplate.Id,
                    targetTemplate.MaxPlayers ?? 30), character, targetMapId),
                targetInstance => Arrive(connection, character, targetInstance, targetTemplate, targetMapId, null,
                    int.MaxValue, reResolved));
        }
        else if (parties?.PartyOf(characterId) is { } party)
        {
            PartyId partyId = party.Id;
            int capacity = Math.Min(world.Configuration.MaxPartySize,
                targetTemplate.MaxPlayers is { } max ? max : world.Configuration.MaxPartySize);

            connection.EnqueueContinuation(
                Settled(world.PartyInstances.GetOrCreatePartyInstanceAsync(partyId, targetTemplate.Id), character,
                    targetMapId),
                targetInstance => Arrive(connection, character, targetInstance, targetTemplate, targetMapId, partyId,
                    capacity, reResolved));
        }
        else
        {
            connection.EnqueueContinuation(
                Settled(world.InstanceRegistry.GetOrCreateNormalInstanceAsync(characterId, targetTemplate.Id), character,
                    targetMapId),
                targetInstance => Arrive(connection, character, targetInstance, targetTemplate, targetMapId, null,
                    int.MaxValue, reResolved));
        }
    }

    /// <summary>
    /// <paramref name="request" /> itself once it has succeeded, which costs nothing; otherwise a task that ends when
    /// it does and never faults: with the instance, or with null when the build faulted or was cancelled. The
    /// connection's continuation drain logs and drops the callback of a task that did not succeed, and a dropped
    /// arrival would leave RespawnInFlight set for good, so the arrival is always given a task whose callback runs, as
    /// TownReturn and MapTeleport give theirs.
    /// </summary>
    /// <remarks>
    /// A failed build is logged here, where it ends and its exception is at hand (on whichever thread ends it, with
    /// the character's name and the map read now, on the tick), as the drain logged it before; the arrival then only
    /// ends the move. Nothing here may throw: a log provider that does (the logger rethrows what its providers throw)
    /// would fault this task after all, and the arrival that clears RespawnInFlight would be dropped with it.
    /// </remarks>
    private Task<IMapInstance?> Settled(Task<IMapInstance> request, ICharacter character, MapId targetMapId)
    {
        if (request.IsCompletedSuccessfully)
        {
            return request!;
        }

        return request.ContinueWith(static IMapInstance? (built, state) =>
            {
                if (built.IsCompletedSuccessfully)
                {
#pragma warning disable MA0045 // the task has completed successfully: reading its result does not block
                    return built.Result;
#pragma warning restore MA0045
                }

                try
                {
                    (ILogger log, string name, MapId map) = ((ILogger, string, MapId))state!;
                    log.LogError(built.Exception, "EnterMap: the instance of map {MapId} could not be built for {Name}",
                        map, name);
                }
                catch (Exception)
                {
                    // The line is lost; the arrival still runs and ends the move.
                }

                return null;
            },
            ((ILogger)logger, character.Name, targetMapId), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    /// The end of an accepted request, on the tick, once its instance request has ended: the arrival (steps 8 to 13)
    /// or whatever stops it. Whichever it is, the move is over and RespawnInFlight is cleared, on every path out, a
    /// throw included, unless the move was handed to a second resolve, which then ends it the same way.
    /// </summary>
    private void Arrive(IWorldConnection connection, ICharacter character, IMapInstance? targetInstance,
        MapTemplate targetTemplate, MapId targetMapId, PartyId? partyId, int capacity, bool reResolved)
    {
        // The character can leave the connection while the instance loads: a select of it on
        // another connection despawns it here and kicks this one. Transferring then would put a
        // discarded entity back into an instance, beside the new session's copy. The flag is left alone: the
        // connection cleared it as the character left, and it may already be the next character's.
        if (!ReferenceEquals(connection.Character, character))
        {
            Log.LeftBeforeInstanceReady(logger, character.Name);
            return;
        }

        // Set when the arrival is handed to a second resolve, which then owns RespawnInFlight.
        bool resolvingAgain = false;
        try
        {
            // A character that died while its instance was being built is not moved: no transfer, no save, as an
            // item's teleport leaves one where it died (MapTeleport). The request ends as a dead character's does in
            // step 1: dropped with no reply, and counted; the character respawns from where it is.
            if (character.IsDead)
            {
                Log.DiedBeforeInstanceReady(logger, character.Name);
                Count(Refusal.Dead);
                return;
            }

            if (targetInstance is null)
            {
                // The build faulted or was cancelled, and was logged as it ended (Settled). The client is told
                // nothing, as before, when the drain dropped the arrival; the character may ask again.
                return;
            }

            // Before the membership and capacity checks: a released instance holds nobody, and the second resolve
            // repeats both.
            if (ArrivesInReleasedInstance(connection, character, targetInstance, targetTemplate, targetMapId, reResolved,
                    out resolvingAgain))
            {
                return;
            }

            if (partyId is not null && PartyRefusal(character, targetInstance, partyId, capacity) is { } refusal)
            {
                Refuse(connection, refusal,
                    refusal == MapTransitionResult.InstanceFull ? Refusal.InstanceFull : Refusal.NotInParty);
                return;
            }

            Enter(connection, character, targetInstance, targetTemplate, targetMapId);
        }
        finally
        {
            if (!resolvingAgain)
            {
                connection.RespawnInFlight = false;
            }
        }
    }

    /// <summary>
    /// The resolved instance was released before the arrival reached it: an abandoned instance's lifetime
    /// (<c>Game:AbandonedInstanceLifetimeMinutes</c>) can run out between the resolve and this continuation, at 0
    /// within a tick (the last member inside a party instance left meanwhile). The character is never added to a
    /// released instance: the first time, the target is resolved again through the same path, true is returned and
    /// <paramref name="resolvingAgain" /> says the move goes on. A second release in a row, which nothing is known to
    /// cause, fails the move rather than resolving forever.
    /// </summary>
    private bool ArrivesInReleasedInstance(IWorldConnection connection, ICharacter character, IMapInstance targetInstance,
        MapTemplate targetTemplate, MapId targetMapId, bool reResolved, out bool resolvingAgain)
    {
        resolvingAgain = false;
        if (world.InstanceRegistry.GetInstanceById(targetInstance.InstanceId) is not null)
            return false;

        if (!reResolved)
        {
            logger.LogInformation(
                "EnterMap: instance {InstanceId} of map {MapId} was released before {Name} arrived; resolving again",
                targetInstance.InstanceId, targetMapId, character.Name);
            ResolveTarget(connection, character, targetTemplate, targetMapId, reResolved: true);
            resolvingAgain = true; // after: a resolve that throws leaves the flag to be cleared by the arrival
            return true;
        }

        logger.LogWarning("EnterMap: instance {InstanceId} of map {MapId} was released again before {Name} arrived",
            targetInstance.InstanceId, targetMapId, character.Name);
        Refuse(connection, MapTransitionResult.MapNotFound, Refusal.InstanceReleased);
        return true;
    }

    /// <summary>
    /// For a party's instance: a character no longer in that party is refused MapNotFound (the party can end, or the
    /// character leave it, while the instance builds: it is not let in as a member), then a full instance
    /// InstanceFull. Null when the arrival may go on. Continuations run one after another on the tick and
    /// TransferPlayer adds at once, so two members arriving in one tick are counted correctly.
    /// </summary>
    private MapTransitionResult? PartyRefusal(ICharacter character, IMapInstance targetInstance, PartyId partyId,
        int capacity)
    {
        if (parties?.PartyOf(character.Guid.Id)?.Id.Equals(partyId) != true)
            return MapTransitionResult.MapNotFound;

        if (targetInstance.PlayerCount >= capacity)
            return MapTransitionResult.InstanceFull;

        return null;
    }

    /// <summary>The arrival itself, steps 8 to 13: every check has passed and the instance is registered.</summary>
    private void Enter(IWorldConnection connection, ICharacter character, IMapInstance targetInstance,
        MapTemplate targetTemplate, MapId targetMapId)
    {
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

        [LoggerMessage(Level = LogLevel.Debug,
            Message = "EnterMap: {Name} died before its target instance was ready; not moved")]
        public static partial void DiedBeforeInstanceReady(ILogger logger, string name);
    }
}
