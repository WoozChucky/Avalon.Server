using System.Collections.Concurrent;
using Avalon.Common.Telemetry;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Maps;
using Avalon.World.Parties;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Threading;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Instances;

/// <summary>
/// Every live instance, and the indexes that route a character to one: towns by map, a character's own Normal
/// instances, a party's instances.
/// </summary>
/// <remarks>
/// <para>
/// <b>Builds run off the tick; everything else runs on it (#639).</b> A build (layout reads, the navmesh bake, the
/// <see cref="MapInstance" /> and its creatures) runs on the thread pool and touches only the instance it is building.
/// When it ends it only queues its outcome. <see cref="PublishFinished" />, which <c>World.Update</c> runs on every tick
/// before the parties and the instances tick, registers each finished instance, writes its index entry, drops the
/// build's pending entry and only then completes the task its requesters hold. A requester that polls that task
/// (a connection continuation: a select's, or a portal entry's arrival, queued to run whatever the build's outcome,
/// <see cref="ISettledContinuations" />) runs in the same tick's flush; one that first settles it through a
/// continuation of its own (<c>TownReturn</c>, <c>MapTeleport</c>, <c>ctx.Then</c>) waits for a thread-pool thread to
/// run that, and runs in the first flush after it. Either way it is answered, never left waiting.
/// Every index (and the instance list hot reload walks) is written on the tick alone. A failed build is published the
/// same way: its requesters get the failure and the next request starts a fresh build.
/// </para>
/// <para>
/// One build per town map, per character and Normal map, and per party and map is in flight at a time (#442):
/// requesters arriving while it runs share it rather than baking a second copy.
/// </para>
/// <para>
/// <b>A build has a deadline (#969).</b> One still running <see cref="BuildDeadline" /> after it started is abandoned
/// on the tick: a timer on the world's clock queues the deadline beside the finished builds, and
/// <see cref="PublishFinished" /> drops the pending entry, so the next request starts a new build, and hands every
/// requester an <see cref="InstanceBuildTimeoutException" />, which each answers through the failure path it takes for
/// any failed build, ending its move. It is counted (<c>world.instance.build_timeouts</c>) and logged at Error, once
/// per build. Nothing is cancelled: the bake runs on, and if it ever ends its outcome is discarded where it is
/// published (an instance it built is disposed, never registered). Nothing is added to a tick that has no deadline due.
/// </para>
/// <para>
/// Tick thread only, except <see cref="ActiveInstances" />, <see cref="GetInstanceById" /> and
/// <see cref="IsPartyInstance" />, which read the instance list, a concurrent dictionary, so telemetry may read it
/// from its own thread. The writers assert the tick thread while <see cref="TickThreadGuard" /> is enabled (#639).
/// </para>
/// <para>
/// <b>Lifetime.</b> A Normal or party instance that has been entered and is now empty (abandoned) is kept for
/// <c>Game:AbandonedInstanceLifetimeMinutes</c> (15 by default): re-entry within it returns the same instance, and the
/// expiry pass frees it once it has passed. At 0 an abandoned instance is never reused and the next tick's pass frees it.
/// One nobody has entered yet keeps the fixed <see cref="UnenteredInstanceLifetime" />, so a build is never freed between
/// its publication and its requester's arrival, which comes in a connection continuation after the pass. Towns never
/// expire here.
/// </para>
/// <para>
/// The tick walks its own copy of the list, <see cref="TickInstances" /> (#851): an array rebuilt only when an
/// instance was published or removed since the last walk, so a tick that changes nothing allocates nothing for it.
/// </para>
/// </remarks>
public class InstanceRegistry : IInstanceRegistry, IPartyInstanceRegistry
{
    /// <summary>
    /// How long a Normal or party instance nobody has entered yet lives: its requester's arrival, or a disbanded
    /// party's orphaned build. Fixed, whatever <c>Game:AbandonedInstanceLifetimeMinutes</c> says.
    /// </summary>
    public static readonly TimeSpan UnenteredInstanceLifetime = TimeSpan.FromMinutes(15);

    /// <summary>The default of <c>Game:AbandonedInstanceLifetimeMinutes</c>, for a registry built without it.</summary>
    public static readonly TimeSpan DefaultAbandonedInstanceLifetime =
        TimeSpan.FromMinutes(GameConfiguration.DefaultAbandonedInstanceLifetimeMinutes);

    /// <summary>
    /// How long a build may run before it is abandoned and its requesters answered with an
    /// <see cref="InstanceBuildTimeoutException" /> (#969, owner decision): long enough for the slowest forest bake under
    /// load (#924), short enough for a player waiting on a move.
    /// </summary>
    public static readonly TimeSpan BuildDeadline = TimeSpan.FromSeconds(30);

    private static readonly KeyValuePair<string, object?> s_townTag = new("map.type", nameof(MapType.Town));
    private static readonly KeyValuePair<string, object?> s_normalTag = new("map.type", nameof(MapType.Normal));

    // A build's deadline passed: queued beside the finished builds, so it is acted on by PublishFinished, on the tick.
    // Runs on whichever thread the clock's timer fires on.
    private static readonly TimerCallback s_deadlinePassed = static state =>
    {
        (InstanceRegistry registry, PendingBuild pending) = ((InstanceRegistry, PendingBuild))state!;
        registry._finished.Enqueue(new FinishedBuild(pending, Outcome: null));
    };

    // The re-entry window for an abandoned instance; World passes the same value to ProcessExpiredInstances.
    private readonly TimeSpan _abandonedLifetime;

    private readonly ConcurrentDictionary<Guid, MapInstance> _instances = new();

    // The tick's own copy of the live instances (#851), in the order they were published, and the array TickInstances
    // hands out. Written with _instances, on the tick alone; the array is replaced, never written in place, so a walk
    // of one is never disturbed by a publish or a removal made during it.
    private readonly List<MapInstance> _tickOrder = [];
    private IMapInstance[] _tickSnapshot = [];
    private bool _tickSnapshotStale;

    // characterId → { templateId → instanceId } for Normal map re-entry. Keyed by character
    // (not account) so different characters on the same account land in different instances
    // even within the re-entry window.
    private readonly Dictionary<uint, Dictionary<MapTemplateId, Guid>> _characterInstanceMap = [];

    // partyId → { templateId → instanceId }: the party's instances (2026-09-30).
    private readonly Dictionary<uint, Dictionary<MapTemplateId, Guid>> _partyInstanceMap = [];

    // Builds in flight (#442), one per town map, per character and map, per party and map.
    private readonly Dictionary<MapTemplateId, PendingBuild> _pendingTownBuilds = [];
    private readonly Dictionary<(uint CharacterId, MapTemplateId TemplateId), PendingBuild> _pendingNormalBuilds = [];
    private readonly Dictionary<(uint PartyId, MapTemplateId TemplateId), PendingBuild> _pendingPartyBuilds = [];

    // Written by a build's last step, on whichever thread it ends on; read only by PublishFinished, on the tick.
    private readonly ConcurrentQueue<FinishedBuild> _finished = new();

    // Builds abandoned at their deadline whose outcome has not come yet (#969): their bakes still hold a thread-pool
    // thread or a bake's memory. Written on the tick; read by the gauge on the exporter's thread.
    private int _abandonedRunning;

    private readonly ILogger<InstanceRegistry> _logger;
    private readonly IAvalonMapManager _mapManager;
    private readonly IChunkLayoutInstanceFactory _chunkLayoutFactory;
    private readonly TickThreadGuard? _tick;
    private readonly BroadcastSchedule? _broadcasts;
    private readonly TimeProvider _time;

    /// <param name="broadcasts">
    /// The world's broadcast schedule (#975, <see cref="InstanceTicker.Broadcasts" />): each instance published takes the
    /// slot with the fewest live instances, and frees it when it is removed or expires. Without one, every instance starts
    /// its broadcast clock at 0.
    /// </param>
    /// <param name="time">The world's clock, which times each build's <see cref="BuildDeadline" />; the system clock when none is given.</param>
    public InstanceRegistry(
        ILoggerFactory loggerFactory,
        IAvalonMapManager mapManager,
        IChunkLayoutInstanceFactory chunkLayoutFactory,
        TickThreadGuard? tickThread = null,
        TimeSpan? abandonedInstanceLifetime = null,
        BroadcastSchedule? broadcasts = null,
        TimeProvider? time = null)
    {
        _logger = loggerFactory.CreateLogger<InstanceRegistry>();
        _abandonedLifetime = abandonedInstanceLifetime ?? DefaultAbandonedInstanceLifetime;
        _mapManager = mapManager;
        _chunkLayoutFactory = chunkLayoutFactory;
        _tick = tickThread;
        _broadcasts = broadcasts;
        _time = time ?? TimeProvider.System;
    }

    public IReadOnlyCollection<IMapInstance> ActiveInstances => _instances.Values.ToList();

    /// <summary>
    /// Builds abandoned at their deadline that are still running (#969): nothing cancels a bake, so under load
    /// abandonments can stack. Any thread; the gauge <c>world.instance.builds_abandoned_running</c> reads it. On the
    /// concrete registry, never on the modding API's <see cref="IInstanceRegistry" />.
    /// </summary>
    public int AbandonedBuildsRunning => Volatile.Read(ref _abandonedRunning);

    /// <summary>
    /// The live instances for <c>World.Update</c> to tick, in the order they were published (#851). The same array tick
    /// after tick, rebuilt only once the set has changed, so it allocates nothing on a tick that published or removed
    /// nothing. A snapshot: an instance published or removed while it is walked joins or leaves the next tick's walk.
    /// Tick thread only; any other reader takes <see cref="ActiveInstances" />. On the concrete registry, never on the
    /// modding API's <see cref="IInstanceRegistry" />. Public rather than internal: the test assemblies are not
    /// strong-named.
    /// </summary>
    public ReadOnlySpan<IMapInstance> TickInstances()
    {
        _tick?.AssertOnTick("InstanceRegistry.TickInstances");

        if (_tickSnapshotStale)
        {
            // A new array, never the old one rewritten: a walk of the old one may still be in progress.
            var snapshot = new IMapInstance[_tickOrder.Count];
            for (int i = 0; i < snapshot.Length; i++)
                snapshot[i] = _tickOrder[i];

            _tickSnapshot = snapshot;
            _tickSnapshotStale = false;
        }

        return _tickSnapshot;
    }

    public Task<IMapInstance> GetOrCreateTownInstanceAsync(MapTemplateId templateId, ushort maxPlayers)
    {
        _tick?.AssertOnTick("InstanceRegistry.GetOrCreateTownInstanceAsync");

        MapInstance? candidate = FindTownWithRoom(templateId, maxPlayers);
        if (candidate is not null)
            return Task.FromResult<IMapInstance>(candidate);

        if (_pendingTownBuilds.TryGetValue(templateId, out PendingBuild? pending))
            return pending.Join();

        _logger.LogInformation("All Town instances for map {TemplateId} are at capacity; creating a new one", templateId);
        pending = new PendingBuild(PendingKind.Town, 0, templateId);
        _pendingTownBuilds[templateId] = pending;
        StartBuild(pending, ownerCharacterId: null, ownerParty: null);
        return pending.Done.Task;
    }

    /// <summary>The least-populated town instance of this map that still has room, if any.</summary>
    private MapInstance? FindTownWithRoom(MapTemplateId templateId, ushort maxPlayers)
    {
        MapInstance? candidate = null;
        int lowestCount = int.MaxValue;

        foreach ((_, MapInstance instance) in _instances)
        {
            if (instance.TemplateId != templateId || instance.MapType != MapType.Town)
            {
                continue;
            }

            if (!instance.CanAcceptPlayer(maxPlayers))
            {
                continue;
            }

            if (instance.PlayerCount < lowestCount)
            {
                lowestCount = instance.PlayerCount;
                candidate = instance;
            }
        }

        return candidate;
    }

    public Task<IMapInstance> GetOrCreateNormalInstanceAsync(uint characterId, MapTemplateId templateId)
    {
        _tick?.AssertOnTick("InstanceRegistry.GetOrCreateNormalInstanceAsync");

        MapInstance? existing = FindReentryInstance(characterId, templateId);
        if (existing is not null)
            return Task.FromResult<IMapInstance>(existing);

        if (_pendingNormalBuilds.TryGetValue((characterId, templateId), out PendingBuild? pending))
            return pending.Join();

        pending = new PendingBuild(PendingKind.Normal, characterId, templateId);
        _pendingNormalBuilds[(characterId, templateId)] = pending;
        StartBuild(pending, characterId, ownerParty: null);
        return pending.Done.Task;
    }

    /// <summary>The character's unexpired instance of this map, if it still has one.</summary>
    private MapInstance? FindReentryInstance(uint characterId, MapTemplateId templateId)
    {
        if (_characterInstanceMap.TryGetValue(characterId, out Dictionary<MapTemplateId, Guid>? characterMap) &&
            characterMap.TryGetValue(templateId, out Guid existingId) &&
            _instances.TryGetValue(existingId, out MapInstance? existing) &&
            !HasExpired(existing, _abandonedLifetime))
        {
            _logger.LogInformation(
                "Returning existing Normal instance {InstanceId} for character {CharacterId}, map {TemplateId}",
                existingId, characterId, templateId);
            return existing;
        }

        return null;
    }

    public Task<IMapInstance> GetOrCreatePartyInstanceAsync(PartyId party, MapTemplateId templateId)
    {
        _tick?.AssertOnTick("InstanceRegistry.GetOrCreatePartyInstanceAsync");

        MapInstance? existing = FindPartyInstance(party.Value, templateId);
        if (existing is not null)
            return Task.FromResult<IMapInstance>(existing);

        if (_pendingPartyBuilds.TryGetValue((party.Value, templateId), out PendingBuild? pending))
            return pending.Join();

        pending = new PendingBuild(PendingKind.Party, party.Value, templateId);
        _pendingPartyBuilds[(party.Value, templateId)] = pending;
        StartBuild(pending, ownerCharacterId: null, party);
        return pending.Done.Task;
    }

    private MapInstance? FindPartyInstance(uint party, MapTemplateId templateId) =>
        _partyInstanceMap.TryGetValue(party, out Dictionary<MapTemplateId, Guid>? maps)
        && maps.TryGetValue(templateId, out Guid id)
        && _instances.TryGetValue(id, out MapInstance? instance)
        && !HasExpired(instance, _abandonedLifetime)
            ? instance
            : null;

    public bool IsPartyInstance(PartyId party, Guid instanceId) =>
        _instances.TryGetValue(instanceId, out MapInstance? instance) && party.Equals(instance.OwnerPartyId);

    public void ForgetParty(PartyId party)
    {
        _tick?.AssertOnTick("InstanceRegistry.ForgetParty");

        _partyInstanceMap.Remove(party.Value);

        // A build still in flight would index its instance once it is published; it is told not to. Left unindexed,
        // its instance empties and expires like any instance nobody enters.
        foreach (((uint PartyId, MapTemplateId) key, PendingBuild pending) in _pendingPartyBuilds)
        {
            if (key.PartyId == party.Value)
                pending.Forgotten = true;
        }
    }

    public IMapInstance? GetInstanceById(Guid instanceId) =>
        _instances.TryGetValue(instanceId, out MapInstance? instance) ? instance : null;

    public void RemoveInstance(Guid instanceId)
    {
        _tick?.AssertOnTick("InstanceRegistry.RemoveInstance");

        // Dispose, not just drop: disposal ends the drops still on the instance's ground.
        if (_instances.TryRemove(instanceId, out MapInstance? instance))
        {
            Untrack(instance);
            instance.Dispose();
            _logger.LogInformation("Instance {InstanceId} removed from registry", instanceId);
        }
    }

    public void ProcessExpiredInstances(TimeSpan abandonedInstanceLifetime)
    {
        _tick?.AssertOnTick("InstanceRegistry.ProcessExpiredInstances");

        foreach ((Guid id, MapInstance instance) in _instances)
        {
            if (instance.MapType != MapType.Normal || instance.PlayerCount > 0 ||
                !HasExpired(instance, abandonedInstanceLifetime))
            {
                continue;
            }

            if (!_instances.TryRemove(id, out MapInstance? removed))
            {
                continue;
            }

            Untrack(removed);

            // Without this the log line below is untrue: the instance stays rooted by the static
            // entity events it subscribed to in its constructor.
            removed.Dispose();

            _logger.LogInformation(
                "Normal map instance {InstanceId} for map {TemplateId} freed after expiry",
                id, instance.TemplateId);

            // Only while each entry still names this instance: a newer build of the map may have replaced it.
            if (instance.OwnerCharacterId is { } character)
                Unindex(_characterInstanceMap, character, instance.TemplateId, id);

            if (instance.OwnerPartyId is { } party)
                Unindex(_partyInstanceMap, party.Value, instance.TemplateId, id);
        }
    }

    /// <summary>
    /// Past its lifetime: <paramref name="abandonedLifetime" /> once it has been entered and emptied, the fixed
    /// <see cref="UnenteredInstanceLifetime" /> while nobody has entered it.
    /// </summary>
    private static bool HasExpired(MapInstance instance, TimeSpan abandonedLifetime) =>
        instance.IsExpired(instance.HasBeenEntered ? abandonedLifetime : UnenteredInstanceLifetime);

    /// <summary>
    /// Publishes every build that has finished since the last call: registers the instance, writes its index entry,
    /// drops the pending entry, then completes the task its requesters hold (a failed build hands them its failure
    /// instead). A build whose deadline passed first is abandoned here (<see cref="Abandon" />), and its outcome, if it
    /// ever comes, discarded (<see cref="Discard" />). Tick thread only; <c>World.Update</c> calls it first thing.
    /// Returns the instances it registered, for the world to bring up to date with the script hot reloads applied while
    /// they were building.
    /// </summary>
    public IReadOnlyList<MapInstance> PublishFinished()
    {
        _tick?.AssertOnTick("InstanceRegistry.PublishFinished");

        if (_finished.IsEmpty)
            return Array.Empty<MapInstance>();

        List<MapInstance> published = [];
        while (_finished.TryDequeue(out FinishedBuild done))
        {
            PendingBuild pending = done.Build;

            // The whole step is contained: whatever throws, the build is not lost, its requesters are answered (with
            // the failure), and the builds behind it are still published.
            try
            {
                if (done.Outcome is not { } outcome)
                {
                    Abandon(pending);
                    continue;
                }

                // Its outcome is here, so its deadline no longer matters; one that fired meanwhile finds it answered.
                pending.Deadline?.Dispose();
                if (pending.Abandoned)
                {
                    Discard(pending, outcome);
                    continue;
                }

                // First, so whatever follows, the next request finds the instance or starts afresh, never this entry.
                Forget(pending);

                if (!outcome.IsCompletedSuccessfully)
                {
                    if (outcome.IsCanceled)
                        pending.Done.TrySetCanceled(CancellationToken.None);
                    else
                        pending.Done.TrySetException(outcome.Exception!.InnerExceptions);
                    continue;
                }

                MapInstance instance = outcome.Result;
                Track(instance);
                Index(pending, instance);

                _logger.LogInformation("Created {MapType} instance {InstanceId} for map {TemplateId}",
                    instance.MapType, instance.InstanceId, pending.TemplateId);
                published.Add(instance);
                pending.Done.TrySetResult(instance);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to publish a build of map {TemplateId}", pending.TemplateId);
                pending.Done.TrySetException(e);
            }
        }

        return published;
    }

    /// <summary>
    /// The build's deadline passed (#969). Unless its outcome was published first, the build is abandoned: its pending
    /// entry dropped, so the next request starts a new build, and every requester handed an
    /// <see cref="InstanceBuildTimeoutException" />, then the timeout counted and logged at Error, once. The build itself
    /// runs on; <see cref="Discard" /> takes its outcome if it ever comes.
    /// </summary>
    private void Abandon(PendingBuild pending)
    {
        if (pending.Done.Task.IsCompleted)
            return; // published (or failed) before the deadline was acted on

        pending.Abandoned = true;
        int stillRunning = Interlocked.Increment(ref _abandonedRunning);
        pending.Deadline?.Dispose();
        Forget(pending);
        pending.Done.TrySetException(
            new InstanceBuildTimeoutException(pending.TemplateId, BuildDeadline, _time.GetUtcNow().UtcDateTime));

        DiagnosticsConfig.World.InstanceBuildTimeouts.Add(1, pending.Kind == PendingKind.Town ? s_townTag : s_normalTag);
        // No instance id: the build has not made its instance yet. Discard names it if it ever does.
        _logger.LogError(
            "The {Kind} build of map {TemplateId} (owner {OwnerId}) did not finish within {DeadlineSeconds} s: abandoned, " +
            "and its {Requesters} requester(s) answered with a failure; {AbandonedRunning} abandoned build(s) still running",
            pending.Kind, pending.TemplateId, pending.OwnerId, BuildDeadline.TotalSeconds, pending.Requesters,
            stillRunning);
    }

    /// <summary>
    /// The outcome of a build abandoned at its deadline: never published. Its requesters were answered at the deadline
    /// and its pending entry is gone, so an instance it built is disposed, as a removed one is, without ever having been
    /// registered or indexed; a failure is only logged, since no requester is left to log it.
    /// </summary>
    private void Discard(PendingBuild pending, Task<MapInstance> outcome)
    {
        // First: it has stopped running, whatever follows.
        Interlocked.Decrement(ref _abandonedRunning);
        TimeSpan took = _time.GetElapsedTime(pending.StartedAt);
        if (outcome.IsCompletedSuccessfully)
        {
#pragma warning disable MA0045 // the task has completed successfully: reading its result does not block
            MapInstance late = outcome.Result;
#pragma warning restore MA0045
            late.Dispose();
            _logger.LogWarning(
                "The abandoned {Kind} build of map {TemplateId} finished after {Seconds:0.0} s; its instance {InstanceId} was discarded",
                pending.Kind, pending.TemplateId, took.TotalSeconds, late.InstanceId);
            return;
        }

        _logger.LogWarning(outcome.Exception,
            "The abandoned {Kind} build of map {TemplateId} ended after {Seconds:0.0} s without an instance",
            pending.Kind, pending.TemplateId, took.TotalSeconds);
    }

    /// <summary>
    /// Registers an instance in the list and in the tick's copy of it, replacing one held under its id, and gives it its
    /// slot on the broadcast schedule (#975), on the tick before its first.
    /// </summary>
    private void Track(MapInstance instance)
    {
        if (_instances.TryGetValue(instance.InstanceId, out MapInstance? held))
            Untrack(held);

        if (_broadcasts is not null)
        {
            TimeSpan phase = _broadcasts.Join(out int slot);
            instance.JoinBroadcastSchedule(slot, phase);
        }

        _instances[instance.InstanceId] = instance;
        _tickOrder.Add(instance);
        _tickSnapshotStale = true;
    }

    /// <summary>Drops an instance taken out of the list from the tick's copy of it.</summary>
    private void Untrack(MapInstance instance)
    {
        if (_tickOrder.Remove(instance))
            _tickSnapshotStale = true;

        // Its broadcast slot is free for the next instance published (#975), whether or not it ever ticked.
        if (instance.BroadcastSlot >= 0)
        {
            _broadcasts?.Leave(instance.BroadcastSlot);
            instance.BroadcastSlot = -1;
        }
    }

    /// <summary>
    /// Starts the build. Its synchronous start (the template lookup and the factory's first step) runs here, as it
    /// always has; the rest runs wherever the factory's awaits take it, and its last step only queues the outcome. A
    /// build still running here gets its deadline (#969): a timer on the world's clock, whose firing only queues it.
    /// </summary>
    private void StartBuild(PendingBuild pending, uint? ownerCharacterId, PartyId? ownerParty)
    {
        pending.StartedAt = _time.GetTimestamp();
        Task<MapInstance> build;
        try
        {
            MapTemplate template = _mapManager.Templates.FindById(pending.TemplateId)
                                   ?? throw new InvalidOperationException($"MapTemplate {pending.TemplateId} not found.");

            // CancellationToken.None: several requesters share this build, so no one requester's token may cancel it
            // for the others.
            build = _chunkLayoutFactory.BuildAsync(template, ownerCharacterId, CancellationToken.None, ownerParty);
        }
        catch (Exception e)
        {
            build = Task.FromException<MapInstance>(e);
        }

        // One box for both callbacks' state.
        object state = (this, pending);

        // Set on the tick, before the outcome can be published (on the tick too), which disposes it.
        if (!build.IsCompleted)
            pending.Deadline = _time.CreateTimer(s_deadlinePassed, state, BuildDeadline, Timeout.InfiniteTimeSpan);

        _ = build.ContinueWith(static (outcome, state) =>
            {
                (InstanceRegistry? registry, PendingBuild? pendingBuild) = ((InstanceRegistry, PendingBuild))state!;
                registry._finished.Enqueue(new FinishedBuild(pendingBuild, outcome));
            },
            state, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void Forget(PendingBuild pending)
    {
        switch (pending.Kind)
        {
            case PendingKind.Town:
                RemoveIfSame(_pendingTownBuilds, pending.TemplateId, pending);
                break;
            case PendingKind.Normal:
                RemoveIfSame(_pendingNormalBuilds, (pending.OwnerId, pending.TemplateId), pending);
                break;
            case PendingKind.Party:
                RemoveIfSame(_pendingPartyBuilds, (pending.OwnerId, pending.TemplateId), pending);
                break;
        }
    }

    private void Index(PendingBuild pending, MapInstance instance)
    {
        switch (pending.Kind)
        {
            case PendingKind.Normal:
                IndexOf(_characterInstanceMap, pending.OwnerId)[pending.TemplateId] = instance.InstanceId;
                break;
            case PendingKind.Party when !pending.Forgotten:
                IndexOf(_partyInstanceMap, pending.OwnerId)[pending.TemplateId] = instance.InstanceId;
                break;
        }
    }

    private static Dictionary<MapTemplateId, Guid> IndexOf(Dictionary<uint, Dictionary<MapTemplateId, Guid>> index,
        uint owner)
    {
        if (!index.TryGetValue(owner, out Dictionary<MapTemplateId, Guid>? maps))
        {
            maps = [];
            index[owner] = maps;
        }

        return maps;
    }

    private static void Unindex(Dictionary<uint, Dictionary<MapTemplateId, Guid>> index, uint owner,
        MapTemplateId templateId, Guid instanceId)
    {
        if (!index.TryGetValue(owner, out Dictionary<MapTemplateId, Guid>? maps)
            || !maps.TryGetValue(templateId, out Guid named) || named != instanceId)
        {
            return;
        }

        maps.Remove(templateId);
        if (maps.Count == 0)
            index.Remove(owner);
    }

    private static void RemoveIfSame<TKey>(Dictionary<TKey, PendingBuild> pending, TKey key, PendingBuild build)
        where TKey : notnull
    {
        if (pending.TryGetValue(key, out PendingBuild? held) && ReferenceEquals(held, build))
            pending.Remove(key);
    }

    private enum PendingKind
    {
        Town,
        Normal,
        Party,
    }

    /// <summary>A build in flight and the task every requester of it holds. Tick thread only.</summary>
    private sealed class PendingBuild(PendingKind kind, uint ownerId, MapTemplateId templateId)
    {
        public PendingKind Kind { get; } = kind;

        /// <summary>The character (Normal) or party (Party) it is built for; 0 for a town.</summary>
        public uint OwnerId { get; } = ownerId;

        public MapTemplateId TemplateId { get; } = templateId;

        /// <summary>Completed on the tick, by <see cref="PublishFinished" />, never by the build itself.</summary>
        public TaskCompletionSource<IMapInstance> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The party was forgotten while this built: its instance is registered but not indexed.</summary>
        public bool Forgotten { get; set; }

        /// <summary>How many requests were handed <see cref="Done" />'s task: the one that started the build, and every one that joined it.</summary>
        public int Requesters { get; private set; } = 1;

        /// <summary>When the build started, as the world clock's timestamp.</summary>
        public long StartedAt { get; set; }

        /// <summary>The timer of its <see cref="BuildDeadline" />, while it runs; disposed once it is published or abandoned.</summary>
        public ITimer? Deadline { get; set; }

        /// <summary>Its deadline passed first (#969): its requesters were answered with the timeout, and its outcome is discarded.</summary>
        public bool Abandoned { get; set; }

        /// <summary>A request joining the build in flight: it waits on the same task.</summary>
        public Task<IMapInstance> Join()
        {
            Requesters++;
            return Done.Task;
        }
    }

    /// <param name="Outcome">The build's task, ended; null when it is the build's deadline that passed.</param>
    private readonly record struct FinishedBuild(PendingBuild Build, Task<MapInstance>? Outcome);
}
