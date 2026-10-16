using System.Collections.Concurrent;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;

namespace Avalon.Api.Identity.Services;

/// <summary>Whether a world takes players now, and its stored player limit (null for none).</summary>
public readonly record struct WorldAvailability(bool Available, int? PlayerLimit);

/// <summary>
/// Whether a world takes players (its row, its ready heartbeat, and no maintenance cutoff in force) and its limit, read
/// at most every 5 s per world (#967): a full world's waiters check in every 5 or 15 s each, and must not each read the
/// row. A world with no row is not available and has no limit.
/// </summary>
public sealed class GameWorldAvailability(IWorldRepository worlds, IWorldReadiness readiness, TimeProvider clock)
{
    private static readonly TimeSpan s_lifetime = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<ushort, (DateTime ReadAtUtc, WorldAvailability Value)> _cache = new();

    public async Task<WorldAvailability> ReadAsync(ushort worldId, CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        if (_cache.TryGetValue(worldId, out (DateTime ReadAtUtc, WorldAvailability Value) cached) && now - cached.ReadAtUtc < s_lifetime)
            return cached.Value;

        Domain.Auth.World? world = await worlds.FindByIdAsync(new WorldId(worldId), false, cancellationToken);
        bool available = world is not null && await readiness.IsReadyAsync(worldId, cancellationToken) &&
            !new WorldMaintenanceState(world.MaintenanceEnabled, world.MaintenanceRevision, world.MaintenanceDeadlineUtc).IsCutoffActive(now);
        var value = new WorldAvailability(available, world?.PlayerLimit);
        _cache[worldId] = (now, value);
        return value;
    }
}
