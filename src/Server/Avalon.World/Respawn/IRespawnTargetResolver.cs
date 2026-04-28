using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Respawn;

public interface IRespawnTargetResolver
{
    /// <summary>
    /// Walks the <see cref="ProceduralMapConfig.BackPortalTargetMapId"/> chain starting at
    /// <paramref name="from"/> until the first <see cref="MapType.Town"/> map is found.
    /// Returns <c>MapTemplateId(1)</c> when the chain is broken (missing config / null target)
    /// or when the cycle-cap of 8 hops is hit. Dying in a town respawns there.
    /// </summary>
    Task<MapTemplateId> ResolveTownAsync(MapTemplateId from, CancellationToken ct);
}

public sealed class RespawnTargetResolver : IRespawnTargetResolver
{
    private const int MaxHops = 8;
    private const ushort FallbackTownId = 1;

    private readonly ILogger<RespawnTargetResolver> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public RespawnTargetResolver(ILoggerFactory loggerFactory, IServiceProvider sp)
    {
        _logger = loggerFactory.CreateLogger<RespawnTargetResolver>();
        _scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
    }

    public async Task<MapTemplateId> ResolveTownAsync(MapTemplateId from, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var mapRepo = scope.ServiceProvider.GetRequiredService<IMapTemplateRepository>();
        var cfgRepo = scope.ServiceProvider.GetRequiredService<IProceduralMapConfigRepository>();

        var current = from;
        for (int hop = 0; hop < MaxHops; hop++)
        {
            var template = await mapRepo.FindByIdAsync(current, false, ct);
            if (template is null) break;
            if (template.MapType == MapType.Town) return current;

            var cfg = await cfgRepo.FindByTemplateIdAsync(current, ct);
            if (cfg is null) break;

            current = new MapTemplateId(cfg.BackPortalTargetMapId);
        }

        _logger.LogWarning(
            "Respawn town walk from map {From} did not reach a town within {MaxHops} hops; falling back to map {Fallback}",
            from.Value, MaxHops, FallbackTownId);
        return new MapTemplateId(FallbackTownId);
    }
}
