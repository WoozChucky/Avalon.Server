using System.Diagnostics;
using Avalon.LoadTest.World;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.World;

namespace Avalon.LoadTest.Bots;

/// <summary>Where a fighter is on its round trip from town through a forest of its own and back.</summary>
public enum FighterState
{
    /// <summary>In town, standing: before its first trip a random 0 to 30 s, after a failure 30 s, otherwise not at all.</summary>
    Town,

    /// <summary>Walking to the town's forest portal.</summary>
    ToPortal,

    /// <summary>Asked the portal for the forest; standing until the world answers.</summary>
    Entering,

    /// <summary>In the forest: walking to the nearest live creature and casting at it in reach.</summary>
    InForest,

    /// <summary>Walking back to the forest's entry, then to the portal behind it.</summary>
    ToExit,

    /// <summary>Asked the back portal for town; standing until the world answers.</summary>
    Leaving,

    /// <summary>Dead: asking to respawn in town until the world moves it there.</summary>
    Dead,
}

/// <summary>What a fighter has sent besides its input on a step; at most one, and only when the bot's last one has gone.</summary>
public enum FighterAction
{
    None,

    /// <summary><c>CMSG_ENTER_MAP</c> for the forest (<see cref="Fighter.ForestMapId"/>).</summary>
    EnterForest,

    /// <summary><c>CMSG_ENTER_MAP</c> for town (<see cref="Fighter.TownMapId"/>).</summary>
    LeaveForest,

    /// <summary><c>CMSG_CAST_ABILITY</c> of the class's basic ability at the aim point.</summary>
    Cast,

    /// <summary><c>CMSG_RESPAWN_AT_TOWN</c>.</summary>
    Respawn,
}

/// <summary>How a fighter's trip ended (<see cref="Fighter.TripEnded"/>).</summary>
public enum TripEnd
{
    /// <summary>It walked out of the forest into town.</summary>
    Completed,

    /// <summary>A step of it failed (<see cref="BotMetrics.FighterFailed"/>).</summary>
    Failed,

    /// <summary>The fighter died and has respawned in town.</summary>
    Died,
}

/// <summary>
/// One step of a fighter: the input to send (a unit direction, or none, and the yaw), whether that heading is a new one
/// rather than the last one steered on, and the packet to send besides (<see cref="FighterAction"/>), with its aim
/// point for a cast.
/// </summary>
public readonly record struct FighterStep(
    float DirX, float DirZ, ushort Yaw, bool NewHeading, FighterAction Action, float AimX, float AimY, float AimZ);

/// <summary>
/// A fighter's round trip, decided step by step: from town it walks to the forest portal, enters a forest of its own,
/// walks to the nearest live creature and casts its class's basic ability at it in reach, and after
/// <c>--forest-time</c> (or a minute with no live creature in sight) walks back out to town, and goes again. Dead, it
/// respawns in town. The input driver asks it for each step at 60 Hz (<see cref="Step"/>) and sends what it decides;
/// it sends nothing itself.
/// </summary>
/// <remarks>
/// <para>
/// What it knows of the world: the acks (its own position and velocity), its <see cref="Table"/> of the objects in view
/// (the creatures, and its own character's death), and the map transitions, cast refusals and its character's death its
/// connection hands it on the read loop (<see cref="OnTransition"/>, <see cref="OnCastRefused"/>,
/// <see cref="OnDamaged"/>). It looks at the table every sixth step (10 Hz, the rate the world sends it), each fighter on
/// its own phase, so the steps of a driver with many fighters stay even; a death it was handed it takes on the next
/// step, so it stops casting in the tick it died, and the table is the fallback for one it was not.
/// </para>
/// <para>
/// It moves by bump-and-turn: it steers straight at its goal, and when an ack shows the last heading stopped against
/// a wall it turns away as a walker does (<see cref="Heading.TurnAway"/>) and holds that heading for a second before
/// steering at the goal again. The forest (chunks of 30 m, with branches) has no straight path from deep inside, so the
/// way out retraces the way in: in the forest the fighter drops a crumb every 4 m it walks (a loop it walks back into
/// is cut out of the trail), and on the way out it steers at the crumbs newest first, then at the entry spawn and the
/// back portal. The trail is a fixed 256 crumbs; when full, every other crumb is dropped, so a longer walk keeps a
/// coarser trail rather than none.
/// </para>
/// <para>
/// After a map transition the acks of inputs sent before it was taken may still describe the old map: until an ack
/// answers an input sent since, the fighter stands, and reads neither its position nor a wall from them.
/// </para>
/// <para>
/// <see cref="Step"/>, <see cref="Reset"/> and the properties the driver reads run on the driver's thread only;
/// <see cref="OnTransition"/>, <see cref="OnCastRefused"/> and <see cref="OnDamaged"/> on the connection's read loop;
/// <see cref="TakeReconnect"/> on the bot's life loop. A step allocates nothing.
/// </para>
/// </remarks>
public sealed class Fighter
{
    /// <summary>The town's map.</summary>
    public const ushort TownMapId = 1;

    /// <summary>The forest's map.</summary>
    public const ushort ForestMapId = 2;

    /// <summary>The default <c>--forest-time</c>.</summary>
    public static readonly TimeSpan DefaultForestTime = TimeSpan.FromMinutes(5);

    /// <summary>Before its first trip, a ramp's fighter waits a random time up to this, so forest bakes do not arrive at once.</summary>
    public static readonly TimeSpan FirstTripJitter = TimeSpan.FromSeconds(30);

    /// <summary>The town's forest portal (its radius is 3 m), straight north of the town's spawn at (15, 15).</summary>
    private const float TownPortalX = 15f;
    private const float TownPortalZ = 45f;

    /// <summary>Where the forest puts a character that enters it.</summary>
    private const float EntrySpawnX = 15f;
    private const float EntrySpawnZ = 15f;

    /// <summary>The forest's portal back to town, straight south of its entry spawn.</summary>
    private const float BackPortalX = 15f;
    private const float BackPortalZ = 5f;

    /// <summary>From the entry spawn to the back portal, straight south: 10 m.</summary>
    private const float EntryToPortal = EntrySpawnZ - BackPortalZ;

    /// <summary>Within this of a portal or the entry spawn (on the ground plane), the fighter is there.</summary>
    private const float ArrivalRadius = 2.5f;

    /// <summary>How far a fighter looks for a creature.</summary>
    private const float SearchRange = 60f;

    /// <summary>
    /// How far inside its ability's reach a fighter stops to cast, at most: 1 m, or a fifth of a short reach. A creature
    /// holds its station 1.5 m from what it fights, so a warrior stopping a metre inside Cleave's 2.5 m would chase it
    /// for ever; at 2 m its swing (whose cone reaches past the target's centre by its body) lands.
    /// </summary>
    private const float ReachMargin = 1f;

    /// <summary>The share of a short reach kept as the margin.</summary>
    private const float ReachMarginShare = 0.2f;

    /// <summary>The most crumbs a trail holds.</summary>
    private const int TrailCapacity = 256;

    /// <summary>A crumb is dropped once the fighter is this far from the last one; and a crumb this near cuts a loop.</summary>
    private const float CrumbSpacing = 4f;

    /// <summary>On the way out, a crumb within this is passed and the next one steered at.</summary>
    private const float CrumbReached = 1.5f;

    /// <summary>A character's base walk speed in m/s (the world's <c>CharacterMovement.BaseSpeed</c>), for the exit's budget.</summary>
    private const float WalkSpeed = 4f;

    /// <summary>The table is looked at every this many steps: 10 Hz, as the world sends it.</summary>
    private const int LookEvery = 6;

    /// <summary>How many creatures cast at are watched for their death at once.</summary>
    private const int CastAtCapacity = 8;

    /// <summary>The way out's stall kinds (<see cref="ExitTimedOut"/>), made once.</summary>
    private const string ExitTimeoutTrail = "forest:exit-timeout:trail";
    private const string ExitTimeoutEntry = "forest:exit-timeout:entry";
    private const string ExitTimeoutPortal = "forest:exit-timeout:portal";
    private const string ExitTimeoutLeaving = "forest:exit-timeout:leaving";

    /// <summary>The basic abilities' 0.8 s cooldown and a margin.</summary>
    private static readonly long s_castInterval = Ticks(TimeSpan.FromMilliseconds(850));

    /// <summary>Not within the portal's radius after this long walking to it, the walk failed.</summary>
    private static readonly long s_portalTimeout = Ticks(TimeSpan.FromSeconds(20));

    /// <summary>After a failure, the fighter stands in town this long before it tries again.</summary>
    private static readonly TimeSpan s_retryPauseTime = TimeSpan.FromSeconds(30);
    private static readonly long s_retryPause = Ticks(s_retryPauseTime);

    /// <summary>
    /// An entry the world has not answered within this long failed: a forest is built off the tick in under a second,
    /// and the world answers every entry it does not drop.
    /// </summary>
    private static readonly long s_enterTimeout = Ticks(TimeSpan.FromSeconds(30));

    /// <summary>No live creature within <see cref="SearchRange"/> for this long, the forest is cleared: the fighter leaves.</summary>
    private static readonly long s_noCreatureTimeout = Ticks(TimeSpan.FromSeconds(60));

    /// <summary>
    /// Not back in town this long after setting out for the exit, the fighter reconnects (in town); a long trail gets
    /// twice its walk instead (<see cref="ExitBudget"/>).
    /// </summary>
    private static readonly TimeSpan s_exitFloor = TimeSpan.FromSeconds(60);

    /// <summary>After the back portal refused, the fighter asks it again no sooner than this.</summary>
    private static readonly long s_leaveRetry = Ticks(TimeSpan.FromSeconds(1));

    /// <summary>
    /// A dead fighter asks to respawn again this often until it is moved: the world drops the ask while a move is under
    /// way, and refuses it while another is.
    /// </summary>
    private static readonly long s_respawnRepeat = Ticks(TimeSpan.FromSeconds(5));

    /// <summary>Still dead this long after dying, the fighter reconnects: a fresh login lands in town.</summary>
    private static readonly long s_respawnTimeout = Ticks(TimeSpan.FromSeconds(30));

    /// <summary>
    /// Held this long after a transition with no ack from the new map (a living character's every input is answered
    /// within a tick or two), the fighter reconnects.
    /// </summary>
    private static readonly long s_staleAckTimeout = Ticks(TimeSpan.FromSeconds(10));

    /// <summary>How long a heading turned away from a wall is held before steering at the goal again.</summary>
    private static readonly long s_detour = Ticks(TimeSpan.FromSeconds(1));

    /// <summary>The names of the cast refusals, made once: a refusal is counted by name without a string made for it.</summary>
    private static readonly string[] s_refusals = [.. Enumerable.Range(0, 256).Select(reason => ((CastRejectReason)reason).ToString())];

    private const int NoTransition = -1;

    /// <summary>
    /// The transition slot's mark of a <see cref="MapTransitionResult.MoveInProgress"/> refusal handed over since the step
    /// last took the slot, above the result's byte: kept when a success replaces the refusal, set on a success still
    /// pending, so the step tells the world's own move from the answer to the fighter's ask however close they land.
    /// </summary>
    private const int MoveRefused = 1 << 24;

    private readonly BotMetrics _metrics;
    private readonly long _forestTime;
    private readonly long _firstTripJitter;
    private readonly float _reach;

    /// <summary>The creatures cast at whose death is still to be seen; 0 is a free slot.</summary>
    private readonly ulong[] _castAt = new ulong[CastAtCapacity];

    /// <summary>The trail's crumbs, oldest first: the way back out of the forest.</summary>
    private readonly float[] _trailX = new float[TrailCapacity];
    private readonly float[] _trailZ = new float[TrailCapacity];

    private volatile FighterState _state = FighterState.Town;

    /// <summary>Set by <see cref="OnDamaged"/> on the read loop, taken by the next step.</summary>
    private volatile bool _killed;

    private volatile TaskCompletionSource _reconnect = NewReconnect();

    /// <summary>
    /// The last transition the read loop handed over and not yet taken: <c>result &lt;&lt; 16 | map</c>, with
    /// <see cref="MoveRefused"/>; <see cref="NoTransition"/> when none is.
    /// </summary>
    private int _transition = NoTransition;
    private long _transitionAt;
    private int _trail;

    /// <summary>The first input whose ack describes the map the fighter is on: the step's when a transition was taken.</summary>
    private uint _freshFrom;

    /// <summary>When the last transition was taken: a hold for a fresh ack counts from it.</summary>
    private long _heldSince;

    private bool _started;
    private bool _reconnecting;
    private long _stateSince;
    private long _leaveTownAt;
    private long _requestedAt;
    private long _forestUntil;
    private long _lastCreatureAt;
    private long _exitBy;
    private long _nextCastAt;
    private long _nextRequestAt;
    private long _nextRespawnAt;
    private bool _pastEntry;

    /// <summary>
    /// The back portal was refused because another move is under way (<see cref="MapTransitionResult.MoveInProgress"/>,
    /// <see cref="MoveRefused"/>): the move to town that follows is that one, not the fighter's. Cleared as it asks the
    /// portal.
    /// </summary>
    private bool _unaskedMove;

    private int _lookIn;
    private bool _hasTarget;
    private TrackedObject _target;
    private float _yaw;
    private bool _detouring;
    private float _detourYaw;
    private long _detourUntil;

    /// <param name="botIndex">The bot's index: it gives the class (<c>index % 4 + 1</c>, as the bot creates its character) and the look phase.</param>
    /// <param name="forestTime">How long a trip stays in the forest (<c>--forest-time</c>).</param>
    /// <param name="firstTripJitter">The most the first trip waits in town: <see cref="FirstTripJitter"/> in a ramp, 0 for one bot's check.</param>
    public Fighter(int botIndex, BotMetrics metrics, TimeSpan forestTime, TimeSpan firstTripJitter)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(botIndex);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(forestTime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(firstTripJitter, TimeSpan.Zero);
        _metrics = metrics;
        _forestTime = Ticks(forestTime);
        _firstTripJitter = Ticks(firstTripJitter);
        (AbilityId, float reach) = (botIndex % 4 + 1) switch
        {
            1 => (200u, 2.5f), // Warrior, Cleave
            2 => (210u, 20f), // Wizard, Arcane Bolt
            3 => (220u, 25f), // Hunter, Quick Shot
            _ => (230u, 18f), // Healer, Smite
        };
        _reach = reach - MathF.Min(ReachMargin, ReachMarginShare * reach);
        _lookIn = botIndex % LookEvery + 1;
    }

    /// <summary>
    /// The most a fighter's way out may take with <paramref name="forestTime"/> in the forest: its exit budget for the
    /// longest trail it can have. The trail is never longer than the walk in, at most <paramref name="forestTime"/> at
    /// the walk speed, and the back portal is <see cref="EntryToPortal"/> beyond the entry spawn: so the longer of
    /// 60 s and twice <paramref name="forestTime"/> plus 5 s.
    /// </summary>
    public static TimeSpan MostExitBudget(TimeSpan forestTime) =>
        ExitBudget((float)(forestTime.TotalSeconds * WalkSpeed) + EntryToPortal);

    /// <summary>The class's basic ability: no cost, a 0.8 s cooldown.</summary>
    public uint AbilityId { get; }

    /// <summary>The objects in the character's view; the bot's connection keeps it (<see cref="WorldConnection.State"/>).</summary>
    public WorldStateTable Table { get; } = new();

    /// <summary>Where the fighter is on its trip.</summary>
    public FighterState State => _state;

    /// <summary>
    /// Whether the fighter may set out from town, asked with the step's time (<see cref="Stopwatch.GetTimestamp"/>) on
    /// each step it is ready to, on the driver's thread; null, the default, is always. A party's member waits here for
    /// its party (<see cref="PartyLink.ReadyToLeave"/>).
    /// </summary>
    public Func<long, bool>? ReadyToLeaveTown { get; set; }

    /// <summary>
    /// Completes when the fighter asks its bot to reconnect: it could not get out of the forest, or could not respawn,
    /// and a fresh login lands in town. The fighter stands still until its next connection (<see cref="Reset"/>).
    /// </summary>
    public Task ReconnectAsked => _reconnect.Task;

    /// <summary>Raised on the driver's thread when a trip ends, however it ended.</summary>
    public event Action<TripEnd>? TripEnded;

    /// <summary>Called on the driver's thread with each stage of a trip and how long it took (for <c>check</c>).</summary>
    public Action<string, TimeSpan>? StepTimed { get; set; }

    /// <summary>Called on the driver's thread with a line about a failure or a death (for <c>check</c>).</summary>
    public Action<string>? Note { get; set; }

    /// <summary>
    /// The connection handed over a map transition, at <paramref name="at"/> (<see cref="Stopwatch.GetTimestamp"/>), on
    /// its read loop after it cleared the table for a success. Taken on the next step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A success not yet taken is never overwritten by a refusal: the move happened, and a refusal arriving right after it
    /// answers an ask the move has made moot (a back portal asked as the world moved the character to town itself is
    /// answered <c>MapNotFound</c> from town, which has no portal to it).
    /// </para>
    /// <para>
    /// A <c>MoveInProgress</c> refusal is still remembered (<see cref="MoveRefused"/>), whichever lands first and however
    /// close: the back portal refused because the world was moving the character itself, so the move into town that
    /// comes with it is not the portal's answer. The world refuses the ask before that move lands, and both may be
    /// handed over before the next step.
    /// </para>
    /// </remarks>
    public void OnTransition(MapTransitionResult result, ushort mapId, long at)
    {
        int transition = ((int)result << 16) | mapId;
        bool success = result == MapTransitionResult.Success;
        int refused = result == MapTransitionResult.MoveInProgress ? MoveRefused : 0;
        if (success) Volatile.Write(ref _transitionAt, at);

        // Only the step takes the slot (back to NoTransition) meanwhile: retried until the write holds.
        int pending = Volatile.Read(ref _transition);
        while (true)
        {
            int mark = pending == NoTransition ? 0 : pending & MoveRefused;
            int next;
            if (success)
            {
                next = transition | mark;
            }
            else if (IsSuccess(pending))
            {
                // The success stands; a MoveInProgress only marks it.
                if (refused == 0) return;

                next = pending | refused;
            }
            else
            {
                next = transition | mark | refused;
            }

            int seen = Interlocked.CompareExchange(ref _transition, next, pending);
            if (seen == pending) return;

            pending = seen;
        }
    }

    /// <summary>The world refused a cast (<c>SMSG_ABILITY_NOT_READY</c>), on the read loop: counted by its reason.</summary>
    public void OnCastRefused(CastRejectReason reason) => _metrics.CastRefused(s_refusals[(byte)reason]);

    /// <summary>
    /// The world told a character of a hit (<c>SMSG_CHARACTER_DAMAGED</c>, sent in the tick of the hit), on the read loop:
    /// one that left the fighter's own character (<paramref name="self"/>) at 0 health is its death, and the next step
    /// enters <see cref="FighterState.Dead"/>, ahead of the table. Any other hit changes nothing.
    /// </summary>
    /// <param name="target">The character hit.</param>
    /// <param name="health">The health it has left.</param>
    /// <param name="self">The guid of the bot's character.</param>
    public void OnDamaged(ulong target, uint health, ulong self)
    {
        if (health == 0 && target == self && self != 0) _killed = true;
    }

    /// <summary>
    /// The bot has a new connection: whatever the fighter was doing on the old one is over, and it starts again from
    /// town, still waiting out a wait it had there (its first trip's, or a retry's).
    /// </summary>
    public void Reset()
    {
        _reconnecting = false;
        _killed = false;
        Volatile.Write(ref _transition, NoTransition);
        _freshFrom = 0;
        _trail = 0;
        Array.Clear(_castAt);
        _hasTarget = false;
        _detouring = false;
        _state = FighterState.Town;
    }

    /// <summary>
    /// Takes the fighter's ask to reconnect, if it made one, before the bot enters again: what woke the bot's life loop,
    /// the ask or the connection's close, the next connection answers it.
    /// </summary>
    public void TakeReconnect()
    {
        if (_reconnect.Task.IsCompleted) _reconnect = NewReconnect();
    }

    /// <summary>
    /// The fighter's step at <paramref name="now"/> (<see cref="Stopwatch.GetTimestamp"/>), from the bot's last ack and
    /// whether it shows the last heading stopped against a wall.
    /// </summary>
    /// <param name="canSend">Whether the bot's last packet besides its input has gone: only then may the step decide on another.</param>
    /// <param name="selfGuid">The bot's character's guid, by which the table tells its death.</param>
    /// <param name="seq">The input this step sends: once a transition is taken, only acks of it and later describe the new map.</param>
    public FighterStep Step(BotAck ack, bool blocked, bool canSend, ulong selfGuid, uint seq, long now, Random rng)
    {
        if (!_started)
        {
            _started = true;
            _leaveTownAt = now + (long)(rng.NextDouble() * _firstTripJitter);
            Enter(FighterState.Town, now);
        }

        if (_reconnecting) return Still(newHeading: false);

        FighterState before = _state;
        // Before the transition: a death the world told of precedes, on the wire, the respawn that ends it. A second
        // kill set between the read and the clear is the same death.
        if (_killed)
        {
            _killed = false;
            if (_state != FighterState.Dead) Die(now);
        }

        TakeTransition(seq, now);
        bool fresh = ack.Seq >= _freshFrom;
        if (--_lookIn <= 0)
        {
            _lookIn = LookEvery;
            Look(ack, selfGuid, fresh, now);
        }

        // A dead character's inputs go unanswered, and a dead fighter reads nothing from the acks: it is never held.
        FighterStep step;
        if (fresh || _state == FighterState.Dead)
        {
            step = Decide(ack, blocked, canSend, now, rng);
        }
        else if (now - _heldSince > s_staleAckTimeout)
        {
            // No timeout of Decide's runs while held: this one ends a hold that the world never answers.
            _metrics.FighterFailed("forest:stale-acks");
            Note?.Invoke("No ack from the new map 10 s after the transition: reconnecting.");
            EndTrip(TripEnd.Failed);
            AskReconnect(now);
            step = Still(newHeading: true);
        }
        else
        {
            step = Still(newHeading: false);
        }

        return _state != before && !step.NewHeading ? step with { NewHeading = true } : step;
    }

    private FighterStep Decide(BotAck ack, bool blocked, bool canSend, long now, Random rng)
    {
        switch (_state)
        {
            case FighterState.Town:
                if (now < _leaveTownAt || ReadyToLeaveTown?.Invoke(now) == false) return Still(newHeading: false);

                Enter(FighterState.ToPortal, now);
                goto case FighterState.ToPortal;

            case FighterState.ToPortal:
                if (Near(ack, TownPortalX, TownPortalZ))
                {
                    if (!canSend) return Still(newHeading: false);

                    StepTimed?.Invoke("to-portal", Elapsed(_stateSince, now));
                    _requestedAt = now;
                    Enter(FighterState.Entering, now);
                    return Act(FighterAction.EnterForest);
                }

                if (now - _stateSince > s_portalTimeout)
                {
                    Fail("forest:portal-timeout", now);
                    return Still(newHeading: true);
                }

                return Walk(ack, TownPortalX, TownPortalZ, blocked, now, rng);

            case FighterState.Entering:
                if (now - _stateSince > s_enterTimeout) Fail("forest:enter:timeout", now);
                return Still(newHeading: false);

            case FighterState.InForest:
                Track(ack.X, ack.Z);
                if (now >= _forestUntil || now - _lastCreatureAt > s_noCreatureTimeout)
                {
                    StepTimed?.Invoke("in-forest", Elapsed(_stateSince, now));
                    _exitBy = now + Ticks(ExitBudget(WayOut(ack)));
                    _pastEntry = false;
                    Enter(FighterState.ToExit, now);
                    goto case FighterState.ToExit;
                }

                return Fight(ack, blocked, canSend, now, rng);

            case FighterState.ToExit:
                if (now >= _exitBy)
                {
                    ExitTimedOut(now);
                    return Still(newHeading: true);
                }

                // Back along the trail, newest crumb first, then by the entry spawn to the back portal.
                while (_trail > 0 && Within(ack, _trailX[_trail - 1], _trailZ[_trail - 1], CrumbReached)) _trail--;
                if (_trail > 0) return Walk(ack, _trailX[_trail - 1], _trailZ[_trail - 1], blocked, now, rng);

                if (!_pastEntry)
                {
                    if (!Near(ack, EntrySpawnX, EntrySpawnZ)) return Walk(ack, EntrySpawnX, EntrySpawnZ, blocked, now, rng);

                    _pastEntry = true;
                    _detouring = false;
                }

                if (!Near(ack, BackPortalX, BackPortalZ)) return Walk(ack, BackPortalX, BackPortalZ, blocked, now, rng);
                if (!canSend || now < _nextRequestAt) return Still(newHeading: false);

                StepTimed?.Invoke("to-exit", Elapsed(_stateSince, now));
                _requestedAt = now;
                _unaskedMove = false;
                Enter(FighterState.Leaving, now);
                return Act(FighterAction.LeaveForest);

            case FighterState.Leaving:
                if (now >= _exitBy)
                {
                    ExitTimedOut(now);
                    return Still(newHeading: true);
                }

                return Still(newHeading: false);

            case FighterState.Dead:
                if (now - _stateSince > s_respawnTimeout)
                {
                    _metrics.FighterFailed("forest:respawn-timeout");
                    Note?.Invoke("Still dead 30 s after dying: reconnecting.");
                    EndTrip(TripEnd.Failed);
                    AskReconnect(now);
                    return Still(newHeading: true);
                }

                if (!canSend || now < _nextRespawnAt) return Still(newHeading: false);

                _nextRespawnAt = now + s_respawnRepeat;
                return Act(FighterAction.Respawn);

            default:
                return Still(newHeading: false);
        }
    }

    /// <summary>In the forest: toward the target, casting at it in reach; with none, north, away from the entry.</summary>
    private FighterStep Fight(BotAck ack, bool blocked, bool canSend, long now, Random rng)
    {
        if (!_hasTarget) return WalkYaw(0f, blocked, now, rng);

        float dx = _target.X - ack.X;
        float dz = _target.Z - ack.Z;
        if (dx * dx + dz * dz > _reach * _reach) return Walk(ack, _target.X, _target.Z, blocked, now, rng);

        // In reach: standing, facing it.
        _yaw = Heading.Toward(dx, dz);
        _detouring = false;
        if (!canSend || now < _nextCastAt) return Still(newHeading: false);

        _nextCastAt = now + s_castInterval;
        _metrics.CastSent();
        Watch(_target.Guid);
        return new FighterStep(0f, 0f, Heading.Wire(_yaw), false, FighterAction.Cast, _target.X, _target.Y, _target.Z);
    }

    /// <summary>The table at 10 Hz: the deaths of the creatures it cast at, its own death, and in the forest a target.</summary>
    private void Look(BotAck ack, ulong selfGuid, bool fresh, long now)
    {
        for (int i = 0; i < _castAt.Length; i++)
        {
            ulong guid = _castAt[i];
            if (guid == 0) continue;

            if (!Table.TryGet(guid, out TrackedObject creature))
            {
                // Out of view: whatever became of it, it is no longer seen.
                _castAt[i] = 0;
            }
            else if (creature.Dead || creature.CurrentHealth <= 0)
            {
                _metrics.KillSeen();
                _castAt[i] = 0;
            }
        }

        // The fallback for a death no hit told of: the table shows it within a tenth of a second.
        if (_state != FighterState.Dead && selfGuid != 0 && Table.TryGet(selfGuid, out TrackedObject self) && self.Dead)
        {
            Die(now);
            return;
        }

        // Targets are looked for from the acked position, which must be on this map.
        if (_state != FighterState.InForest || !fresh) return;

        _hasTarget = Table.TryNearestLiveCreature(ack.X, ack.Z, SearchRange, out _target);
        if (_hasTarget) _lastCreatureAt = now;
    }

    /// <summary>The character died: counted, and whatever the fighter was doing gives way to asking for a respawn at once.</summary>
    private void Die(long now)
    {
        _metrics.OwnDeath();
        Note?.Invoke($"Died ({_state}); respawning in town.");
        _nextRespawnAt = now;
        _hasTarget = false;
        Enter(FighterState.Dead, now);
    }

    /// <summary>The transition the read loop handed over, if any, applied to the trip.</summary>
    private void TakeTransition(uint seq, long now)
    {
        int transition = Interlocked.Exchange(ref _transition, NoTransition);
        if (transition == NoTransition) return;

        var result = (MapTransitionResult)((transition >> 16) & 0xff);
        ushort mapId = (ushort)transition;
        long at = Volatile.Read(ref _transitionAt);
        // The back portal was refused for a move of the world's own, before or with whatever is taken now: the move into
        // town that lands is that one (cleared as the fighter next asks the portal).
        if ((transition & MoveRefused) != 0 && _state == FighterState.Leaving) _unaskedMove = true;

        if (result == MapTransitionResult.Success)
        {
            // A new instance, a new view: the connection cleared the table, and nothing cast at is in it any more. The
            // acks describe it from this step's input on.
            Array.Clear(_castAt);
            _hasTarget = false;
            _freshFrom = seq;
            _heldSince = now;
            _trail = 0;
            if (mapId == ForestMapId)
            {
                if (_state == FighterState.Entering)
                {
                    TimeSpan entry = Elapsed(_requestedAt, at);
                    _metrics.ForestEntered(entry);
                    StepTimed?.Invoke("enter-map", entry);
                }

                _forestUntil = now + _forestTime;
                _lastCreatureAt = now;
                Enter(FighterState.InForest, now);
                return;
            }

            if (_state == FighterState.Leaving && !_unaskedMove)
            {
                StepTimed?.Invoke("exit-map", Elapsed(_requestedAt, at));
                _metrics.ForestTripCompleted();
                EndTrip(TripEnd.Completed);
            }
            else if (_state == FighterState.Dead)
            {
                StepTimed?.Invoke("respawn", Elapsed(_stateSince, at));
                EndTrip(TripEnd.Died);
            }
            else if (_state != FighterState.Town)
            {
                // Moved out of a trip it had not finished by a move it did not ask for: a party that fell apart has the
                // world return its members from the party's forest after a countdown.
                Fail("forest:returned", now);
                return;
            }

            // Back in town (or on a map a fighter does not know, from which its walk to the portal times out).
            _leaveTownAt = now;
            Enter(FighterState.Town, now);
            return;
        }

        // A refused respawn is asked again (Dead); any other refusal answers nothing this fighter asked.
        if (_state == FighterState.Entering)
        {
            Fail($"forest:enter:{result}", now);
        }
        else if (_state == FighterState.Leaving && result == MapTransitionResult.MoveInProgress)
        {
            // Another move is under way: the world's (marked above), which ends the trip when it lands (or the exit
            // budget does).
            Note?.Invoke("The back portal refused: another move is under way; waiting for it.");
        }
        else if (_state == FighterState.Leaving)
        {
            // The way out is still open while its exit budget lasts: back to the portal, and ask again.
            _metrics.FighterFailed($"forest:leave:{result}");
            Note?.Invoke($"The back portal refused: {result}.");
            _pastEntry = true;
            _nextRequestAt = now + s_leaveRetry;
            Enter(FighterState.ToExit, now);
        }
    }

    /// <summary>A failed step: counted, and back to town to try again after the pause.</summary>
    private void Fail(string kind, long now)
    {
        _metrics.FighterFailed(kind);
        Note?.Invoke($"Failed: {kind}; trying again in {s_retryPauseTime.TotalSeconds:0} s.");
        EndTrip(TripEnd.Failed);
        _leaveTownAt = now + s_retryPause;
        Enter(FighterState.Town, now);
    }

    /// <summary>
    /// Not back in town within the exit budget: counted by where the way out stalled (crumbs left, short of the entry
    /// spawn, short of the back portal, or asked and not answered), and the fighter reconnects.
    /// </summary>
    private void ExitTimedOut(long now)
    {
        string kind = _state == FighterState.Leaving ? ExitTimeoutLeaving
            : _trail > 0 ? ExitTimeoutTrail
            : !_pastEntry ? ExitTimeoutEntry
            : ExitTimeoutPortal;
        _metrics.FighterFailed(kind);
        Note?.Invoke($"Not back in town within its exit budget ({kind}): reconnecting.");
        EndTrip(TripEnd.Failed);
        AskReconnect(now);
    }

    /// <summary>
    /// Asks the bot's life loop to reconnect (<see cref="ReconnectAsked"/>), and stands still in town until it has. A
    /// living character cannot ask to respawn, and a fresh login lands in town.
    /// </summary>
    private void AskReconnect(long now)
    {
        _reconnecting = true;
        Enter(FighterState.Town, now);
        _reconnect.TrySetResult();
    }

    private void EndTrip(TripEnd end) => TripEnded?.Invoke(end);

    private void Enter(FighterState state, long now)
    {
        _state = state;
        _stateSince = now;
        _detouring = false;
    }

    /// <summary>A creature cast at, watched for its death; with every slot taken, the oldest watch is dropped.</summary>
    private void Watch(ulong guid)
    {
        int free = -1;
        for (int i = 0; i < _castAt.Length; i++)
        {
            if (_castAt[i] == guid) return;
            if (free < 0 && _castAt[i] == 0) free = i;
        }

        if (free < 0)
        {
            Array.Copy(_castAt, 1, _castAt, 0, _castAt.Length - 1);
            free = _castAt.Length - 1;
        }

        _castAt[free] = guid;
    }

    /// <summary>Steers at (<paramref name="x"/>, <paramref name="z"/>) from the acked position, bumping and turning.</summary>
    private FighterStep Walk(BotAck ack, float x, float z, bool blocked, long now, Random rng) =>
        WalkYaw(Heading.Toward(x - ack.X, z - ack.Z), blocked, now, rng);

    /// <summary>
    /// Steers along <paramref name="goalYaw"/>; after a step the ack shows blocked, along a heading turned away from it
    /// for <see cref="s_detour"/>, turning again if that one is blocked too.
    /// </summary>
    private FighterStep WalkYaw(float goalYaw, bool blocked, long now, Random rng)
    {
        bool turned = false;
        if (blocked)
        {
            _detourYaw = Heading.TurnAway(_detouring ? _detourYaw : goalYaw, rng);
            _detourUntil = now + s_detour;
            _detouring = true;
            turned = true;
        }
        else if (_detouring && now >= _detourUntil)
        {
            _detouring = false;
            turned = true;
        }

        _yaw = _detouring ? _detourYaw : goalYaw;
        (float dirX, float dirZ) = Heading.Direction(_yaw);
        return new FighterStep(dirX, dirZ, Heading.Wire(_yaw), turned, FighterAction.None, 0f, 0f, 0f);
    }

    private FighterStep Still(bool newHeading) =>
        new(0f, 0f, Heading.Wire(_yaw), newHeading, FighterAction.None, 0f, 0f, 0f);

    private FighterStep Act(FighterAction action) =>
        new(0f, 0f, Heading.Wire(_yaw), true, action, 0f, 0f, 0f);

    private static bool Near(BotAck ack, float x, float z) => Within(ack, x, z, ArrivalRadius);

    private static bool Within(BotAck ack, float x, float z, float radius) => Within(ack.X, ack.Z, x, z, radius);

    private static bool Within(float fromX, float fromZ, float x, float z, float radius)
    {
        float dx = x - fromX;
        float dz = z - fromZ;
        return dx * dx + dz * dz <= radius * radius;
    }

    private static float Distance(float fromX, float fromZ, float x, float z) =>
        MathF.Sqrt((x - fromX) * (x - fromX) + (z - fromZ) * (z - fromZ));

    private static bool IsSuccess(int transition) =>
        transition != NoTransition && (MapTransitionResult)((transition >> 16) & 0xff) == MapTransitionResult.Success;

    /// <summary>
    /// The trail, from the acked position: a crumb once the fighter is <see cref="CrumbSpacing"/> from the last; back at
    /// an older crumb, the loop since is cut out. Full, every other crumb is dropped (the oldest and newest kept).
    /// </summary>
    private void Track(float x, float z)
    {
        if (_trail > 0)
        {
            if (Within(x, z, _trailX[_trail - 1], _trailZ[_trail - 1], CrumbSpacing)) return;

            for (int i = 0; i < _trail - 1; i++)
            {
                if (!Within(x, z, _trailX[i], _trailZ[i], CrumbSpacing)) continue;

                _trail = i + 1;
                return;
            }

            if (_trail == TrailCapacity) Thin();
        }

        _trailX[_trail] = x;
        _trailZ[_trail] = z;
        _trail++;
    }

    /// <summary>Halves a full trail: every other crumb from the oldest, and the newest.</summary>
    private void Thin()
    {
        int kept = 0;
        for (int i = 0; i < _trail; i += 2, kept++)
        {
            _trailX[kept] = _trailX[i];
            _trailZ[kept] = _trailZ[i];
        }

        if ((_trail - 1) % 2 != 0)
        {
            _trailX[kept] = _trailX[_trail - 1];
            _trailZ[kept] = _trailZ[_trail - 1];
            kept++;
        }

        _trail = kept;
    }

    /// <summary>How far the way out is from the acked position: along the trail, then by the entry spawn to the back portal.</summary>
    private float WayOut(BotAck ack)
    {
        float length = 0f;
        (float x, float z) = (ack.X, ack.Z);
        for (int i = _trail - 1; i >= 0; i--)
        {
            length += Distance(x, z, _trailX[i], _trailZ[i]);
            (x, z) = (_trailX[i], _trailZ[i]);
        }

        return length + Distance(x, z, EntrySpawnX, EntrySpawnZ) + EntryToPortal;
    }

    /// <summary>The exit budget for a way out <paramref name="wayOut"/> metres long: twice its walk, and 60 s at least.</summary>
    private static TimeSpan ExitBudget(float wayOut)
    {
        var walk = TimeSpan.FromSeconds(2 * wayOut / WalkSpeed);
        return walk > s_exitFloor ? walk : s_exitFloor;
    }

    private static TaskCompletionSource NewReconnect() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static long Ticks(TimeSpan span) => (long)(span.TotalSeconds * Stopwatch.Frequency);

    private static TimeSpan Elapsed(long from, long to) => TimeSpan.FromSeconds((double)(to - from) / Stopwatch.Frequency);
}
