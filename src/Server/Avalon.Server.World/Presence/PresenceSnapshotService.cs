using Avalon.Infrastructure;
using Avalon.Infrastructure.Presence;
using Avalon.World.Configuration;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Server.World.Presence;

/// <summary>
/// Publishes a live view of who is in which instance, for admin observability tooling.
///
/// Runs on its own ~1 Hz timer, deliberately NOT on the 60 Hz simulation tick: a slow
/// Redis round trip must never stall the world update. This type is a strictly
/// read-only observer of <see cref="IInstanceRegistry"/> — it must never mutate
/// simulation state, so a defect here cannot corrupt gameplay.
///
/// Keys carry a short TTL (<see cref="CacheKeys.PresenceTtl"/>) refreshed on every
/// capture. If this server dies or hangs, its keys expire and its players simply
/// disappear from the admin view. That is the intended failure mode: show nothing
/// rather than present a stale position as if it were live.
/// </summary>
public sealed class PresenceSnapshotService : BackgroundService
{
    private static readonly TimeSpan CaptureInterval = TimeSpan.FromSeconds(1);

    private readonly Func<IInstanceRegistry?> _registryAccessor;
    private readonly IReplicatedCache _cache;
    private readonly ILogger<PresenceSnapshotService> _logger;
    private readonly ushort _worldId;

    public PresenceSnapshotService(
        IInstanceRegistry registry,
        IReplicatedCache cache,
        IOptions<GameConfiguration> gameConfig,
        ILogger<PresenceSnapshotService> logger)
        : this(() => registry, cache, gameConfig, logger)
    {
    }

    /// <summary>
    /// Re-reads the registry from <paramref name="registryAccessor"/> on every capture rather
    /// than capturing a single instance once. <c>IWorld.InstanceRegistry</c> does not exist
    /// until <c>World.LoadAsync</c> runs inside <c>WorldServer.ExecuteAsync</c> — well after
    /// every <see cref="IHostedService"/>, including this one, has already been constructed
    /// by the generic host (the host resolves the full <c>IEnumerable&lt;IHostedService&gt;</c>
    /// before calling <c>StartAsync</c> on any of them). Capturing
    /// <c>IWorld.InstanceRegistry</c> once at construction time would therefore permanently
    /// bind to <see langword="null"/>. Production wiring (<c>Program.cs</c>) uses this overload
    /// with an accessor that reads <c>IWorld.InstanceRegistry</c> fresh on every tick instead —
    /// which can itself still return <see langword="null"/> for the first several ticks while
    /// <c>LoadAsync</c> is still running (DB and asset loading takes seconds); see
    /// <see cref="CaptureOnceAsync"/>, which treats that as "nothing to publish yet", not a fault.
    /// </summary>
    public PresenceSnapshotService(
        Func<IInstanceRegistry?> registryAccessor,
        IReplicatedCache cache,
        IOptions<GameConfiguration> gameConfig,
        ILogger<PresenceSnapshotService> logger)
    {
        _registryAccessor = registryAccessor;
        _cache = cache;
        _logger = logger;
        _worldId = gameConfig.Value.WorldId.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CaptureInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CaptureOnceAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Captures and publishes one snapshot. Public so tests can drive a single cycle
    /// without waiting on the timer. Never throws — a failed publish is logged and the
    /// next tick retries. Writes are whole-value overwrites, so nothing accumulates.
    /// </summary>
    public async Task CaptureOnceAsync(CancellationToken ct)
    {
        try
        {
            IInstanceRegistry? registry = _registryAccessor();
            if (registry is null)
            {
                // IWorld.InstanceRegistry does not exist until World.LoadAsync completes --
                // several seconds of DB/asset loading that this service's first few ticks
                // race against on every boot. Expected, not a Redis/observability fault,
                // so this stays at debug rather than the warning below.
                _logger.LogDebug(
                    "Presence capture skipped for world {WorldId}: instance registry not ready yet.", _worldId);
                return;
            }

            DateTime now = DateTime.UtcNow;
            List<InstancePresenceSnapshot> instances = BuildInstanceSnapshots(registry, now);
            if (instances.Count == 0) return;

            var snapshot = new WorldPresenceSnapshot(_worldId, now, instances);
            await _cache.SetAsync(
                CacheKeys.WorldPresence(_worldId),
                PresenceJson.Serialize(snapshot),
                CacheKeys.PresenceTtl);

            await WriteCharacterIndexAsync(instances);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Only our own caller's shutdown token should unwind out of this hosted
            // service. Any other cancellation observed here (e.g. a Redis client
            // surfacing a TaskCanceledException from a reconnect/dispose race, which
            // derives from OperationCanceledException) falls through to the logging
            // catch below instead of propagating into BackgroundService and stopping
            // the world server.
            throw;
        }
        catch (Exception ex)
        {
            // Observability must never take the world server down. Next tick retries.
            _logger.LogWarning(ex, "Presence snapshot capture failed for world {WorldId}", _worldId);
        }
    }

    /// <summary>
    /// Maps every active instance that currently holds at least one player into its wire
    /// snapshot. Empty instances (town hubs sit empty for long stretches) are skipped —
    /// they carry no observability value and would only inflate the payload.
    /// </summary>
    private List<InstancePresenceSnapshot> BuildInstanceSnapshots(IInstanceRegistry registry, DateTime now)
    {
        List<InstancePresenceSnapshot> instances = [];

        foreach (IMapInstance instance in registry.ActiveInstances)
        {
            InstancePresenceSnapshot? snapshot = TryBuildInstanceSnapshot(instance, now);
            if (snapshot is not null) instances.Add(snapshot);
        }

        return instances;
    }

    /// <summary>
    /// Builds one instance's snapshot, or null if it currently holds no players or its
    /// roster could not be safely read this tick.
    ///
    /// <c>ISimulationContext.Characters</c> is a plain dictionary mutated by the 60 Hz
    /// simulation tick thread (<c>AddCharacter</c>/<c>RemoveCharacter</c>) while this timer
    /// walks it from a thread-pool thread. A player entering or leaving mid-walk can surface
    /// as <see cref="InvalidOperationException"/> ("Collection was modified"). That is
    /// contained to this one instance for this one tick — it must not discard every other
    /// instance's data — and is expected under normal player movement, not a fault worth a
    /// warning. Fixing the underlying race belongs to the simulation's instance/character
    /// collections, not this read-only observer.
    /// </summary>
    private InstancePresenceSnapshot? TryBuildInstanceSnapshot(IMapInstance instance, DateTime now)
    {
        try
        {
            List<CharacterPresenceSnapshot> characters = [];
            foreach (ICharacter c in instance.Characters.Values)
            {
                characters.Add(new CharacterPresenceSnapshot(
                    CharacterId: c.Guid.Id,
                    Name: c.Name,
                    Class: c.Class.ToString(),
                    X: c.Position.x, Y: c.Position.y, Z: c.Position.z,
                    Orientation: c.Orientation.y,
                    Level: c.Level,
                    CurrentHealth: c.CurrentHealth,
                    Health: c.Health,
                    MoveState: c.MoveState.ToString(),
                    InCombat: c.IsInCombat,
                    Dead: c.IsDead,
                    LastSeen: now));
            }

            if (characters.Count == 0) return null;

            return new InstancePresenceSnapshot(
                InstanceId: instance.InstanceId,
                TemplateId: instance.TemplateId.Value,
                Seed: instance.Seed,
                MapType: instance.MapType.ToString(),
                ConfigVersion: instance.ConfigVersion,
                OwnerCharacterId: instance.OwnerCharacterId,
                Characters: characters);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex,
                "Presence capture skipped instance {InstanceId}: roster changed mid-walk.", instance.InstanceId);
            return null;
        }
    }

    /// <summary>
    /// Writes the character -> (world, instance) reverse index used by the Api to find a
    /// player without scanning every world snapshot. Fired off in parallel rather than
    /// awaited one at a time — at hundreds of concurrent players, serial round trips could
    /// eat a meaningful fraction of the 1-second capture budget and risk missing the
    /// <see cref="CacheKeys.PresenceTtl"/> window on a latency spike.
    /// </summary>
    private Task WriteCharacterIndexAsync(List<InstancePresenceSnapshot> instances)
    {
        List<Task> writes = [];
        foreach (InstancePresenceSnapshot instance in instances)
        {
            foreach (CharacterPresenceSnapshot c in instance.Characters)
            {
                writes.Add(_cache.SetAsync(
                    CacheKeys.CharacterPresenceIndex(c.CharacterId),
                    PresenceJson.Serialize(new CharacterPresenceIndex(_worldId, instance.InstanceId)),
                    CacheKeys.PresenceTtl));
            }
        }

        return Task.WhenAll(writes);
    }
}
