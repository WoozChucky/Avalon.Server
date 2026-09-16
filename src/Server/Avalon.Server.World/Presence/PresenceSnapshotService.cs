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

    private readonly Func<IInstanceRegistry> _registryAccessor;
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
    /// with an accessor that reads <c>IWorld.InstanceRegistry</c> fresh on every tick instead.
    /// </summary>
    public PresenceSnapshotService(
        Func<IInstanceRegistry> registryAccessor,
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
            DateTime now = DateTime.UtcNow;
            List<InstancePresenceSnapshot> instances = BuildInstanceSnapshots(_registryAccessor(), now);
            if (instances.Count == 0) return;

            var snapshot = new WorldPresenceSnapshot(_worldId, now, instances);
            await _cache.SetAsync(
                CacheKeys.WorldPresence(_worldId),
                PresenceJson.Serialize(snapshot),
                CacheKeys.PresenceTtl);

            await WriteCharacterIndexAsync(instances);
        }
        catch (OperationCanceledException)
        {
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
    private static List<InstancePresenceSnapshot> BuildInstanceSnapshots(IInstanceRegistry registry, DateTime now)
    {
        List<InstancePresenceSnapshot> instances = [];

        foreach (IMapInstance instance in registry.ActiveInstances)
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

            if (characters.Count == 0) continue;

            instances.Add(new InstancePresenceSnapshot(
                InstanceId: instance.InstanceId,
                TemplateId: instance.TemplateId.Value,
                Seed: instance.Seed,
                MapType: instance.MapType.ToString(),
                ConfigVersion: instance.ConfigVersion,
                OwnerCharacterId: instance.OwnerCharacterId,
                Characters: characters));
        }

        return instances;
    }

    /// <summary>Writes the character -> (world, instance) reverse index used by the Api to find a player without scanning every world snapshot.</summary>
    private async Task WriteCharacterIndexAsync(List<InstancePresenceSnapshot> instances)
    {
        foreach (InstancePresenceSnapshot instance in instances)
        {
            foreach (CharacterPresenceSnapshot c in instance.Characters)
            {
                await _cache.SetAsync(
                    CacheKeys.CharacterPresenceIndex(c.CharacterId),
                    PresenceJson.Serialize(new CharacterPresenceIndex(_worldId, instance.InstanceId)),
                    CacheKeys.PresenceTtl);
            }
        }
    }
}
