using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.ChunkLayouts;

public interface IChunkLibrary
{
    Task LoadAsync(CancellationToken ct);
    ChunkTemplate GetById(ChunkTemplateId id);
    IReadOnlyList<ChunkPoolMember> GetByPool(ChunkPoolId poolId);
    IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> LookupByIds(IEnumerable<ChunkTemplateId> ids);
}

public record ChunkPoolMember(ChunkTemplate Template, float Weight);

public class ChunkLibrary : IChunkLibrary
{
    private readonly ILogger<ChunkLibrary> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private Dictionary<ChunkTemplateId, ChunkTemplate> _templates = new();
    private Dictionary<ChunkPoolId, List<ChunkPoolMember>> _pools = new();

    public ChunkLibrary(ILoggerFactory loggerFactory, IServiceScopeFactory scopeFactory)
    {
        _logger = loggerFactory.CreateLogger<ChunkLibrary>();
        _scopeFactory = scopeFactory;
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var templateRepo = scope.ServiceProvider.GetRequiredService<IChunkTemplateRepository>();
        var poolRepo     = scope.ServiceProvider.GetRequiredService<IChunkPoolRepository>();
        var configRepo   = scope.ServiceProvider.GetRequiredService<IProceduralMapConfigRepository>();

        var templates = await templateRepo.FindAllWithSlotsAsync(ct);
        _templates = templates.ToDictionary(t => t.Id);

        var pools = await poolRepo.FindAllWithMembershipsAsync(ct);
        _pools = pools.ToDictionary(
            p => p.Id,
            p => p.Memberships
                .Where(m => _templates.ContainsKey(m.ChunkTemplateId))
                .Select(m => new ChunkPoolMember(_templates[m.ChunkTemplateId], m.Weight))
                .ToList());

        var configs = await configRepo.FindAllAsync(ct);
        foreach (var cfg in configs) ValidatePool(cfg);

        _logger.LogInformation("Chunk library loaded: {Templates} templates, {Pools} pools, {Configs} configs",
            _templates.Count, _pools.Count, configs.Count);
    }

    public ChunkTemplate GetById(ChunkTemplateId id) =>
        _templates.TryGetValue(id, out var t) ? t : throw new KeyNotFoundException($"ChunkTemplate {id.Value}");

    public IReadOnlyList<ChunkPoolMember> GetByPool(ChunkPoolId poolId) =>
        _pools.TryGetValue(poolId, out var list) ? list : Array.Empty<ChunkPoolMember>();

    public IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> LookupByIds(IEnumerable<ChunkTemplateId> ids)
    {
        var result = new Dictionary<ChunkTemplateId, ChunkTemplate>();
        foreach (var id in ids)
        {
            if (!_templates.TryGetValue(id, out var t))
                throw new KeyNotFoundException($"ChunkTemplate {id.Value}");
            result[id] = t;
        }
        return result;
    }

    private void ValidatePool(ProceduralMapConfig cfg)
    {
        if (!_pools.TryGetValue(cfg.ChunkPoolId, out var members) || members.Count == 0)
            throw new InvalidProceduralConfigException($"Pool {cfg.ChunkPoolId.Value} empty or missing for map {cfg.MapTemplateId.Value}");

        if (!members.Any(m => HasSlotTag(m.Template, "entry") && HasPortalRole(m.Template, PortalRole.Back)))
            throw new InvalidProceduralConfigException(
                $"Pool {cfg.ChunkPoolId.Value} contains no entry chunk (needs Spawn_Entry + Portal_Back) for map {cfg.MapTemplateId.Value}");

        if (cfg.HasBoss && !members.Any(m => HasSlotTag(m.Template, "boss")))
            throw new InvalidProceduralConfigException(
                $"Map {cfg.MapTemplateId.Value} HasBoss but pool has no boss-capable chunk");

        if (cfg.ForwardPortalTargetMapId is not null && !members.Any(m => HasPortalRole(m.Template, PortalRole.Forward)))
            throw new InvalidProceduralConfigException(
                $"Map {cfg.MapTemplateId.Value} ForwardPortalTargetMapId set but no chunk has Portal_Forward slot");

        if (cfg.MainPathMin < 2 || cfg.MainPathMax < cfg.MainPathMin || cfg.MainPathMax > 32)
            throw new InvalidProceduralConfigException(
                $"Map {cfg.MapTemplateId.Value} path length constraints invalid ({cfg.MainPathMin}..{cfg.MainPathMax})");
    }

    private static bool HasSlotTag(ChunkTemplate t, string tag) =>
        t.SpawnSlots.Any(s => string.Equals(s.Tag, tag, StringComparison.OrdinalIgnoreCase));

    private static bool HasPortalRole(ChunkTemplate t, PortalRole role) =>
        t.PortalSlots.Any(p => p.Role == role);
}
