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

    /// <summary>The pool's set pieces (forest content pass); empty for a pool without any.</summary>
    IReadOnlyList<ChunkGroupDefinition> GetGroupsByPool(ChunkPoolId poolId);
}

public class ChunkLibrary : IChunkLibrary
{
    private readonly ILogger<ChunkLibrary> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private Dictionary<ChunkTemplateId, ChunkTemplate> _templates = new();
    private Dictionary<ChunkPoolId, List<ChunkPoolMember>> _pools = new();
    private Dictionary<ChunkPoolId, List<ChunkGroupDefinition>> _groups = new();

    public ChunkLibrary(ILoggerFactory loggerFactory, IServiceScopeFactory scopeFactory)
    {
        _logger = loggerFactory.CreateLogger<ChunkLibrary>();
        _scopeFactory = scopeFactory;
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IChunkTemplateRepository templateRepo = scope.ServiceProvider.GetRequiredService<IChunkTemplateRepository>();
        IChunkPoolRepository poolRepo = scope.ServiceProvider.GetRequiredService<IChunkPoolRepository>();
        IProceduralMapConfigRepository configRepo = scope.ServiceProvider.GetRequiredService<IProceduralMapConfigRepository>();

        IReadOnlyList<ChunkTemplate> templates = await templateRepo.FindAllWithSlotsAsync(ct);
        _templates = templates.ToDictionary(t => t.Id);

        IReadOnlyList<ChunkPool> pools = await poolRepo.FindAllWithMembershipsAsync(ct);
        _pools = pools.ToDictionary(
            p => p.Id,
            p => p.Memberships
                .Where(m => _templates.ContainsKey(m.ChunkTemplateId))
                .Select(m => new ChunkPoolMember(_templates[m.ChunkTemplateId], m.Weight))
                .ToList());

        _groups = pools.ToDictionary(p => p.Id, LoadGroups);

        IReadOnlyList<ProceduralMapConfig> configs = await configRepo.FindAllAsync(ct);
        foreach (ProceduralMapConfig cfg in configs) ValidatePool(cfg);

        _logger.LogInformation("Chunk library loaded: {Templates} templates, {Pools} pools, {Configs} configs",
            _templates.Count, _pools.Count, configs.Count);
    }

    public ChunkTemplate GetById(ChunkTemplateId id) =>
        _templates.TryGetValue(id, out ChunkTemplate? t) ? t : throw new KeyNotFoundException($"ChunkTemplate {id.Value}");

    public IReadOnlyList<ChunkPoolMember> GetByPool(ChunkPoolId poolId) =>
        _pools.TryGetValue(poolId, out List<ChunkPoolMember>? list) ? list : Array.Empty<ChunkPoolMember>();

    public IReadOnlyList<ChunkGroupDefinition> GetGroupsByPool(ChunkPoolId poolId) =>
        _groups.TryGetValue(poolId, out List<ChunkGroupDefinition>? list) ? list : Array.Empty<ChunkGroupDefinition>();

    public IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> LookupByIds(IEnumerable<ChunkTemplateId> ids)
    {
        var result = new Dictionary<ChunkTemplateId, ChunkTemplate>();
        foreach (ChunkTemplateId id in ids)
        {
            if (!_templates.TryGetValue(id, out ChunkTemplate? t))
                throw new KeyNotFoundException($"ChunkTemplate {id.Value}");
            result[id] = t;
        }
        return result;
    }

    /// <summary>
    /// The pool's set pieces. One with no members or a member template that is not loaded is left out, with a warning:
    /// otherwise a missing boss arena surfaces only as "pool has no boss-capable chunk".
    /// </summary>
    private List<ChunkGroupDefinition> LoadGroups(ChunkPool pool)
    {
        var groups = new List<ChunkGroupDefinition>(pool.Groups.Count);
        foreach (ChunkGroup group in pool.Groups)
        {
            if (ChunkGroupDefinition.From(group, _templates) is { } definition)
            {
                groups.Add(definition);
            }
            else
            {
                _logger.LogWarning(
                    "Set piece '{Group}' of pool {Pool} is left out: it has no members or names a chunk template that is not loaded",
                    group.Name, pool.Id.Value);
            }
        }

        return groups;
    }

    private void ValidatePool(ProceduralMapConfig cfg)
    {
        if (!_pools.TryGetValue(cfg.ChunkPoolId, out List<ChunkPoolMember>? members) || members.Count == 0)
            throw new InvalidProceduralConfigException($"Pool {cfg.ChunkPoolId.Value} empty or missing for map {cfg.MapTemplateId.Value}");

        if (!members.Any(m => HasSlotTag(m.Template, "entry") && HasPortalRole(m.Template, PortalRole.Back)))
        {
            throw new InvalidProceduralConfigException(
                $"Pool {cfg.ChunkPoolId.Value} contains no entry chunk (needs Spawn_Entry + Portal_Back) for map {cfg.MapTemplateId.Value}");
        }

        IReadOnlyList<ChunkGroupDefinition> groups = GetGroupsByPool(cfg.ChunkPoolId);

        if (cfg.HasBoss && !members.Any(m => HasSlotTag(m.Template, "boss")) && !groups.Any(g => g.IsBoss))
        {
            throw new InvalidProceduralConfigException(
                $"Map {cfg.MapTemplateId.Value} HasBoss but pool has no boss-capable chunk");
        }

        if (cfg.ForwardPortalTargetMapId is not null && !members.Any(m => HasPortalRole(m.Template, PortalRole.Forward))
            && !groups.Any(g => g.HasForward))
        {
            throw new InvalidProceduralConfigException(
                $"Map {cfg.MapTemplateId.Value} ForwardPortalTargetMapId set but no chunk has Portal_Forward slot");
        }

        if (cfg.MainPathMin < 2 || cfg.MainPathMax < cfg.MainPathMin || cfg.MainPathMax > 32)
        {
            throw new InvalidProceduralConfigException(
                $"Map {cfg.MapTemplateId.Value} path length constraints invalid ({cfg.MainPathMin}..{cfg.MainPathMax})");
        }

        if (DepthBandLevels.Problem(cfg.DepthBands) is { } bandProblem)
        {
            throw new InvalidProceduralConfigException(
                $"Map {cfg.MapTemplateId.Value} depth bands are invalid: {bandProblem}");
        }
    }

    private static bool HasSlotTag(ChunkTemplate t, string tag) =>
        t.SpawnSlots.Any(s => string.Equals(s.Tag, tag, StringComparison.OrdinalIgnoreCase));

    private static bool HasPortalRole(ChunkTemplate t, PortalRole role) =>
        t.PortalSlots.Any(p => p.Role == role);
}
