using Avalon.Common.Mathematics;
using Avalon.Common.Telemetry;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using Avalon.Network.Packets.World;
using Avalon.World.Entities;
using Avalon.World.Instances;
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
/// anything that throws on the way. The one ending that leaves it alone is the character having left the connection
/// meanwhile: the connection cleared it as the character left, and it may already be the next character's. The
/// arrival is queued to run whatever becomes of the instance's build (<see cref="ISettledContinuations" />), so no
/// outcome of the build can leave the move without its ending.
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

        // 2 to 6, the checks the portal's menu makes too (PortalChecks): the current instance, the target map's template,
        // a portal to it on the current map, the portal's radius, the map's level band.
        ushort targetMap = packet.TargetMapId;
        PortalCheck portal = PortalChecks.Check(world, character, targetMap);
        if (portal.Refusal != PortalRefusal.None)
        {
            RefuseAtPortal(connection, character, targetMap, portal);
            return;
        }

        MapTemplate targetTemplate = portal.Template!;

        // 7. A choice other than Default (#1104), checked before anything is built: an instance picked by id, or a new
        // one. A plain entry reads nothing of it.
        MapEntryChoice choice = packet.Choice;
        IMapInstance? picked = null;
        Party? party = null;
        if (choice != MapEntryChoice.Default
            && RefusesChoice(connection, character, targetTemplate, packet, out picked, out party))
        {
            return;
        }

        // Resolve the target instance; the rest runs once it is ready. The request is accepted, so the move is under
        // way from here: a second portal entry in this same pass, before the first one's continuation has run, is
        // refused in step 1 rather than arriving and saving once more.
        connection.RespawnInFlight = true;
        try
        {
            if (choice == MapEntryChoice.Default)
                ResolveTarget(connection, character, targetTemplate, targetMap, reResolved: false);
            else
                ResolveChoice(connection, character, targetTemplate, targetMap, choice, picked, party);
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
    /// A refusal of steps 2 to 6: the Debug line of each a client can cause by what it sends (the Error line of a
    /// character in no live instance, the server's own fault), the answer, and the count under the step's reason.
    /// </summary>
    private void RefuseAtPortal(IWorldConnection connection, ICharacter character, ushort targetMap, PortalCheck portal)
    {
        string reason;
        switch (portal.Refusal)
        {
            case PortalRefusal.UnknownMap:
                Log.MapNotFound(logger, targetMap);
                reason = Refusal.UnknownMap;
                break;
            case PortalRefusal.NoInstance:
                logger.LogError("EnterMap: no live map instance for character {Name}; refusing teleport",
                    character.Name);
                reason = Refusal.NoInstance;
                break;
            case PortalRefusal.NoPortal:
                Log.NoPortal(logger, targetMap);
                reason = Refusal.NoPortal;
                break;
            case PortalRefusal.NotNearPortal:
                Log.TooFarFromPortal(logger, character.Name, targetMap, portal.Distance, portal.Portal!.Radius);
                reason = Refusal.NotNearPortal;
                break;
            case PortalRefusal.LevelTooLow:
                reason = Refusal.LevelTooLow;
                break;
            default:
                reason = Refusal.LevelTooHigh;
                break;
        }

        Refuse(connection, portal.Result, reason);
    }

    /// <summary>
    /// One more refused portal entry in <c>world.portal_entry.refusals</c>, under <paramref name="reason" />: one tag,
    /// passed as a struct, so a refusal still allocates nothing.
    /// </summary>
    private static void Count(string reason) =>
        DiagnosticsConfig.World.PortalEntryRefusals.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>
    /// The <c>reason</c> a refused portal entry is counted under: a fixed set, one per place the handler refuses,
    /// since <c>MapNotFound</c> alone answers six of them. Since the refusals log at Debug, the count is what shows
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

        /// <summary>The packet's <c>Choice</c> is none the server knows (<c>MapNotFound</c>): a bad request.</summary>
        public const string UnknownChoice = "unknown_choice";

        /// <summary>A choice other than Default for a town, which has no instances to choose (<c>NotInstanced</c>).</summary>
        public const string NotInstanced = "not_instanced";

        /// <summary>
        /// The picked instance is not one the character may enter, or was released before the arrival
        /// (<c>InstanceUnavailable</c>).
        /// </summary>
        public const string InstanceUnavailable = "instance_unavailable";

        /// <summary>A new instance asked for inside the character's creation cooldown (<c>CreateOnCooldown</c>).</summary>
        public const string CreateOnCooldown = "create_on_cooldown";

        /// <summary>A new party instance asked for by a member who is not the leader (<c>NotPartyLeader</c>).</summary>
        public const string NotPartyLeader = "not_party_leader";

        /// <summary>A new party instance asked for by a character in no party (<c>NotInParty</c>).</summary>
        public const string CreateNotInParty = "create_not_in_party";
    }

    /// <summary>
    /// The checks of a choice other than Default (#1104), in order: one the server knows, for a Normal map; for
    /// <c>Existing</c>, an instance the character may enter (<see cref="IInstanceChoices.FindChoosable" />: its own
    /// private one or its current party's, live, of this map; one answer for every other id); for <c>NewParty</c>, a
    /// party the character leads; for a create, outside the character's creation cooldown. Sends the refusal and returns
    /// true when one fails; otherwise <paramref name="picked" /> is the instance an <c>Existing</c> choice named, and
    /// <paramref name="party" /> the character's party, read once for the checks and the request alike.
    /// Nothing here starts or extends the cooldown.
    /// </summary>
    private bool RefusesChoice(IWorldConnection connection, ICharacter character, MapTemplate targetTemplate,
        CEnterMapPacket packet, out IMapInstance? picked, out Party? party)
    {
        picked = null;
        party = null;
        MapEntryChoice choice = packet.Choice;
        if (choice is not (MapEntryChoice.Existing or MapEntryChoice.NewPrivate or MapEntryChoice.NewParty))
        {
            Log.Refused(logger, character.Name, packet.TargetMapId, "UnknownChoice");
            Refuse(connection, MapTransitionResult.MapNotFound, Refusal.UnknownChoice);
            return true;
        }

        if (targetTemplate.MapType != MapType.Normal)
        {
            Log.Refused(logger, character.Name, packet.TargetMapId, nameof(MapTransitionResult.NotInstanced));
            Refuse(connection, MapTransitionResult.NotInstanced, Refusal.NotInstanced);
            return true;
        }

        uint characterId = character.Guid.Id;
        party = parties?.PartyOf(characterId);
        if (choice == MapEntryChoice.Existing)
        {
            picked = world.InstanceChoices.FindChoosable(characterId, party?.Id, targetTemplate.Id, packet.InstanceId);
            if (picked is not null)
                return false;

            Log.Refused(logger, character.Name, packet.TargetMapId, nameof(MapTransitionResult.InstanceUnavailable));
            Refuse(connection, MapTransitionResult.InstanceUnavailable, Refusal.InstanceUnavailable);
            return true;
        }

        if (choice == MapEntryChoice.NewParty && party is null)
        {
            Log.Refused(logger, character.Name, packet.TargetMapId, nameof(MapTransitionResult.NotInParty));
            Refuse(connection, MapTransitionResult.NotInParty, Refusal.CreateNotInParty);
            return true;
        }

        if (choice == MapEntryChoice.NewParty && !party!.IsLeader(characterId))
        {
            Log.Refused(logger, character.Name, packet.TargetMapId, nameof(MapTransitionResult.NotPartyLeader));
            Refuse(connection, MapTransitionResult.NotPartyLeader, Refusal.NotPartyLeader);
            return true;
        }

        // A create. The time left goes out as a system line, since the transition has no field for it, once per window:
        // a create refused again inside it is answered with the result alone, which allocates nothing.
        if (character is CharacterEntity entity && entity.Clock.GetUtcNow() is var now && now < entity.InstanceCreateAllowedAt)
        {
            Log.Refused(logger, character.Name, packet.TargetMapId, nameof(MapTransitionResult.CreateOnCooldown));
            Refuse(connection, MapTransitionResult.CreateOnCooldown, Refusal.CreateOnCooldown);
            if (entity.InstanceCreateCooldownTold != entity.InstanceCreateAllowedAt)
            {
                entity.InstanceCreateCooldownTold = entity.InstanceCreateAllowedAt;
                int seconds = (int)Math.Ceiling((entity.InstanceCreateAllowedAt - now).TotalSeconds);
                connection.Send(SChatMessagePacket.System(
                    $"You can open a new instance in {seconds} {(seconds == 1 ? "second" : "seconds")}.", now.UtcDateTime,
                    PacketEncoder.Shared));
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// The request for an accepted choice other than Default (#1104), and its arrival queued as a plain entry's is: the
    /// instance picked, which is the party's when it is one of the character's current party's (its arrival then re-checks
    /// the party and the seats), or a new one built for the character or for the party it leads. A create starts the
    /// character's cooldown here, as it is accepted, whatever becomes of its build.
    /// </summary>
    private void ResolveChoice(IWorldConnection connection, ICharacter character, MapTemplate targetTemplate,
        MapId targetMapId, MapEntryChoice choice, IMapInstance? picked, Party? party)
    {
        uint characterId = character.Guid.Id;
        PartyId? partyId = null;
        Task<IMapInstance> request;
        if (choice == MapEntryChoice.Existing)
        {
            if (party is not null && world.PartyInstances.IsPartyInstance(party.Id, picked!.InstanceId))
                partyId = party.Id;

            request = Task.FromResult(picked!);
        }
        else
        {
            if (character is CharacterEntity entity)
            {
                entity.InstanceCreateAllowedAt =
                    entity.Clock.GetUtcNow().AddSeconds(world.Configuration.InstanceCreateCooldownSeconds);
            }

            if (choice == MapEntryChoice.NewParty)
            {
                partyId = party!.Id;
                request = world.InstanceChoices.CreatePartyInstanceAsync(partyId, targetTemplate.Id);
            }
            else
            {
                request = world.InstanceChoices.CreatePrivateInstanceAsync(characterId, targetTemplate.Id);
            }
        }

        int capacity = partyId is null ? int.MaxValue : PartyCapacity(targetTemplate);
        SettledOf(connection).EnqueueSettled(request,
            done => Arrive(connection, character, done, targetTemplate, targetMapId, partyId, capacity, reResolved: false,
                choice));
    }

    /// <summary>The seats of a party's instance of the map: <c>min(MaxPartySize, MaxPlayers ?? MaxPartySize)</c>.</summary>
    private int PartyCapacity(MapTemplate targetTemplate) =>
        Math.Min(world.Configuration.MaxPartySize,
            targetTemplate.MaxPlayers is { } max ? max : world.Configuration.MaxPartySize);

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
        // the closure it always did, with nothing of a party's in it. Each is queued to run whatever becomes of the
        // request (ISettledContinuations): a build that fails must still reach the arrival, which ends the move.
        if (targetTemplate.MapType == MapType.Town)
        {
            SettledOf(connection).EnqueueSettled(
                world.InstanceRegistry.GetOrCreateTownInstanceAsync(targetTemplate.Id, targetTemplate.MaxPlayers ?? 30),
                request => Arrive(connection, character, request, targetTemplate, targetMapId, null, int.MaxValue,
                    reResolved, MapEntryChoice.Default));
        }
        else if (parties?.PartyOf(characterId) is { } party)
        {
            PartyId partyId = party.Id;
            int capacity = PartyCapacity(targetTemplate);

            SettledOf(connection).EnqueueSettled(
                world.PartyInstances.GetOrCreatePartyInstanceAsync(partyId, targetTemplate.Id),
                request => Arrive(connection, character, request, targetTemplate, targetMapId, partyId, capacity,
                    reResolved, MapEntryChoice.Default));
        }
        else
        {
            SettledOf(connection).EnqueueSettled(
                world.InstanceRegistry.GetOrCreateNormalInstanceAsync(characterId, targetTemplate.Id),
                request => Arrive(connection, character, request, targetTemplate, targetMapId, null, int.MaxValue,
                    reResolved, MapEntryChoice.Default));
        }
    }

    /// <summary>
    /// The connection's queue for a continuation that runs whatever its task's outcome. Every connection the server
    /// makes has one; a connection without it cannot take a portal entry, and the request fails as a throw does
    /// anywhere else on its way: the move is ended where it was started or resolved again, and the throw logged.
    /// </summary>
    private static ISettledContinuations SettledOf(IWorldConnection connection) =>
        connection as ISettledContinuations
        ?? throw new InvalidOperationException("The connection cannot queue a portal entry's arrival: it is not the server's own.");

    /// <summary>
    /// The end of an accepted request, on the tick, once its instance request has ended: the arrival (steps 8 to 13)
    /// or whatever stops it. Whichever it is, the move is over and RespawnInFlight is cleared, on every path out, a
    /// throw included, unless the move was handed to a second resolve, which then ends it the same way.
    /// </summary>
    /// <param name="choice">What the request chose (#1104): Default for a plain entry, which arrives as it always has.</param>
    private void Arrive(IWorldConnection connection, ICharacter character, Task<IMapInstance> request,
        MapTemplate targetTemplate, MapId targetMapId, PartyId? partyId, int capacity, bool reResolved,
        MapEntryChoice choice)
    {
        // The character can leave the connection while the instance loads: a select of it on
        // another connection despawns it here and kicks this one. Transferring then would put a
        // discarded entity back into an instance, beside the new session's copy. The flag is left alone: the
        // connection cleared it as the character left, and it may already be the next character's. A build that
        // failed is still logged: nobody else logs it. A log that throws here goes to the drain, which contains it,
        // and no flag is left set by it: this one is not the arrival's to clear.
        if (!ReferenceEquals(connection.Character, character))
        {
            LogIfFailed(request, character, targetMapId);
            Log.LeftBeforeInstanceReady(logger, character.Name);
            return;
        }

        // Set when the arrival is handed to a second resolve, which then owns RespawnInFlight.
        bool resolvingAgain = false;
        try
        {
            // A build that faulted or was cancelled is logged first, whatever else ends the request: the client is
            // sent no transition for it, as when the connection's drain logged it and dropped the arrival, and the
            // character may ask again. A build abandoned at its deadline (#969) is the one failure the player is told
            // of, by a system line. Inside the try: a log that throws still ends the move.
            bool built = !LogIfFailed(request, character, targetMapId);
            if (!built)
                InstanceBuildTimeoutException.TellIfTimedOut(connection, request.Exception);

            // A character that died while its instance was being built is not moved: no transfer, no save, as an
            // item's teleport leaves one where it died (MapTeleport). The request ends as a dead character's does in
            // step 1: dropped with no reply, and counted; the character respawns from where it is.
            if (character.IsDead)
            {
                Log.DiedBeforeInstanceReady(logger, character.Name);
                Count(Refusal.Dead);
                return;
            }

            if (!built)
            {
                return;
            }

#pragma warning disable MA0045 // the task has completed successfully: reading its result does not block
            IMapInstance targetInstance = request.Result;
#pragma warning restore MA0045

            // Before the membership and capacity checks: a released instance holds nobody, and the second resolve
            // repeats both. A chosen instance is never swapped for another: released, it is refused.
            if (choice != MapEntryChoice.Default
                    ? ChosenInstanceReleased(connection, character, targetInstance, targetMapId)
                    : ArrivesInReleasedInstance(connection, character, targetInstance, targetTemplate, targetMapId,
                        reResolved, out resolvingAgain))
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
            if (choice != MapEntryChoice.Default)
                ArrivedByChoice(character, targetInstance, targetTemplate, choice, partyId);
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
    /// Logs at Error, once, that <paramref name="request" />'s build faulted or was cancelled, and says whether it did:
    /// nothing else logs it, since the registry only hands the failure to its requesters. A build abandoned at its
    /// deadline is the exception (#969): the registry logged that at Error, once for all its requesters, so here it is a
    /// Warning, without the stack.
    /// </summary>
    private bool LogIfFailed(Task<IMapInstance> request, ICharacter character, MapId targetMapId)
    {
        if (request.IsCompletedSuccessfully)
        {
            return false;
        }

        if (InstanceBuildTimeoutException.Of(request.Exception) is not null)
        {
            logger.LogWarning("EnterMap: the instance of map {MapId} for {Name} was not built in time; the move failed",
                targetMapId, character.Name);
            return true;
        }

        logger.LogError(request.Exception, "EnterMap: the instance of map {MapId} could not be built for {Name}",
            targetMapId, character.Name);
        return true;
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
    /// A chosen instance (#1104) released before the arrival reached it: refused <c>InstanceUnavailable</c>, as a pick of
    /// it would now be, and never resolved again to another instance, which the character did not choose.
    /// </summary>
    private bool ChosenInstanceReleased(IWorldConnection connection, ICharacter character, IMapInstance targetInstance,
        MapId targetMapId)
    {
        if (world.InstanceRegistry.GetInstanceById(targetInstance.InstanceId) is not null)
            return false;

        logger.LogInformation("EnterMap: chosen instance {InstanceId} of map {MapId} was released before {Name} arrived",
            targetInstance.InstanceId, targetMapId, character.Name);
        Refuse(connection, MapTransitionResult.InstanceUnavailable, Refusal.InstanceUnavailable);
        return true;
    }

    /// <summary>
    /// After an arrival by choice (#1104): the instance becomes its owner's default, the one a plain entry takes, for a
    /// private one picked or opened, and for the party's new one its leader opened, whose other online members are then
    /// told. A member going back to an older party instance leaves the party's default where it is: only the leader's
    /// arrival in a new one moves it.
    /// </summary>
    private void ArrivedByChoice(ICharacter character, IMapInstance targetInstance, MapTemplate targetTemplate,
        MapEntryChoice choice, PartyId? partyId)
    {
        if (choice == MapEntryChoice.Existing && partyId is not null)
            return;

        world.InstanceChoices.MakeDefault(targetInstance);

        uint characterId = character.Guid.Id;
        if (choice == MapEntryChoice.NewParty && parties?.PartyOf(characterId) is { } party)
            parties.SendLine(party, $"{character.Name} opened a new {targetTemplate.Name}.", exceptCharacterId: characterId);
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
            // Through the entity, which keeps the id it parses (#977): TransferPlayer above has set it already, so
            // nothing is formatted again.
            entity.InstanceId = targetInstance.InstanceId;

            // Through the entity, which writes the row and keeps the target's spatial index where the character is
            // (#982); the row written alone would move the character but leave its index entry where it stood.
            entity.Position = spawn;

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

        /// <summary>
        /// A choice other than Default refused (#1104); <paramref name="result" /> is a constant naming why, never a value
        /// the packet carries.
        /// </summary>
        [LoggerMessage(Level = LogLevel.Debug, Message = "EnterMap: {Name}'s choice for map {TargetMapId} refused: {Result}")]
        public static partial void Refused(ILogger logger, string name, ushort targetMapId, string result);
    }
}
