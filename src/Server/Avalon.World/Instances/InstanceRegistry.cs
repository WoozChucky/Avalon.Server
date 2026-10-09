using System.Collections.Concurrent;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
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
/// (a connection continuation) runs in the same tick's flush; one that first settles it through a continuation of its
/// own (<c>TownReturn</c>, <c>ctx.Then</c>) runs a tick later. Either way it is answered, never left waiting.
/// Every index (and the instance list hot reload walks) is written on the tick alone. A failed build is published the
/// same way: its requesters get the failure and the next request starts a fresh build.
/// </para>
/// <para>
/// One build per town map, per character and Normal map, and per party and map is in flight at a time (#442):
/// requesters arriving while it runs share it rather than baking a second copy.
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
    public static readonly TimeSpan DefaultAbandonedInstanceLifetime = TimeSpan.FromMinutes(15);

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

    private readonly ILogger<InstanceRegistry> _logger;
    private readonly IAvalonMapManager _mapManager;
    private readonly IChunkLayoutInstanceFactory _chunkLayoutFactory;
    private readonly TickThreadGuard? _tick;

    public InstanceRegistry(
        ILoggerFactory loggerFactory,
        IAvalonMapManager mapManager,
        IChunkLayoutInstanceFactory chunkLayoutFactory,
        TickThreadGuard? tickThread = null,
        TimeSpan? abandonedInstanceLifetime = null)
    {
        _logger = loggerFactory.CreateLogger<InstanceRegistry>();
        _abandonedLifetime = abandonedInstanceLifetime ?? DefaultAbandonedInstanceLifetime;
        _mapManager = mapManager;
        _chunkLayoutFactory = chunkLayoutFactory;
        _tick = tickThread;
    }

    public IReadOnlyCollection<IMapInstance> ActiveInstances => _instances.Values.ToList();

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
            return pending.Done.Task;

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
            return pending.Done.Task;

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
            return pending.Done.Task;

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
    /// instead). Tick thread only; <c>World.Update</c> calls it first thing. Returns the instances it registered, for
    /// the world to bring up to date with the script hot reloads applied while they were building.
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
                // First, so whatever follows, the next request finds the instance or starts afresh, never this entry.
                Forget(pending);

                if (!done.Outcome.IsCompletedSuccessfully)
                {
                    if (done.Outcome.IsCanceled)
                        pending.Done.TrySetCanceled(CancellationToken.None);
                    else
                        pending.Done.TrySetException(done.Outcome.Exception!.InnerExceptions);
                    continue;
                }

                MapInstance instance = done.Outcome.Result;
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

    /// <summary>Registers an instance in the list and in the tick's copy of it, replacing one held under its id.</summary>
    private void Track(MapInstance instance)
    {
        if (_instances.TryGetValue(instance.InstanceId, out MapInstance? held))
            Untrack(held);

        _instances[instance.InstanceId] = instance;
        _tickOrder.Add(instance);
        _tickSnapshotStale = true;
    }

    /// <summary>Drops an instance taken out of the list from the tick's copy of it.</summary>
    private void Untrack(MapInstance instance)
    {
        if (_tickOrder.Remove(instance))
            _tickSnapshotStale = true;
    }

    /// <summary>
    /// Starts the build. Its synchronous start (the template lookup and the factory's first step) runs here, as it
    /// always has; the rest runs wherever the factory's awaits take it, and its last step only queues the outcome.
    /// </summary>
    private void StartBuild(PendingBuild pending, uint? ownerCharacterId, PartyId? ownerParty)
    {
        Task<MapInstance> build;
        try
        {
            MapTemplate template = _mapManager.Templates.FirstOrDefault(t => t.Id == pending.TemplateId)
                                   ?? throw new InvalidOperationException($"MapTemplate {pending.TemplateId} not found.");

            // CancellationToken.None: several requesters share this build, so no one requester's token may cancel it
            // for the others.
            build = _chunkLayoutFactory.BuildAsync(template, ownerCharacterId, CancellationToken.None, ownerParty);
        }
        catch (Exception e)
        {
            build = Task.FromException<MapInstance>(e);
        }

        _ = build.ContinueWith(static (outcome, state) =>
            {
                (InstanceRegistry? registry, PendingBuild? pendingBuild) = ((InstanceRegistry, PendingBuild))state!;
                registry._finished.Enqueue(new FinishedBuild(pendingBuild, outcome));
            },
            (this, pending), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
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
    }

    private readonly record struct FinishedBuild(PendingBuild Build, Task<MapInstance> Outcome);
}
