using System.Collections.Concurrent;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.Maps;
using Avalon.World.Maps.Navigation;
using Avalon.World.Maps.Virtualized;
using Avalon.World.Pools;
using Avalon.World.ChunkLayouts;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Instances;

public class InstanceRegistry : IInstanceRegistry
{
    // accountId → { templateId → instanceId } for Normal map re-entry
    private readonly ConcurrentDictionary<long, Dictionary<MapTemplateId, Guid>> _accountInstanceMap = new();
    private readonly ConcurrentDictionary<Guid, MapInstance> _instances = new();

    private readonly ILogger<InstanceRegistry> _logger;
    private readonly IAvalonMapManager _mapManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly IChunkLayoutInstanceFactory _chunkLayoutFactory;
    private readonly ProceduralChunkLayoutSource _proceduralSource;
    private readonly IServiceScopeFactory _scopeFactory;

    // Cache of loaded VirtualizedMap data per template, populated lazily
    private readonly ConcurrentDictionary<MapTemplateId, VirtualizedMap> _virtualMapCache = new();

    public InstanceRegistry(
        ILoggerFactory loggerFactory,
        IAvalonMapManager mapManager,
        IServiceProvider serviceProvider,
        IChunkLayoutInstanceFactory chunkLayoutFactory,
        ProceduralChunkLayoutSource proceduralSource,
        IServiceScopeFactory scopeFactory)
    {
        _logger = loggerFactory.CreateLogger<InstanceRegistry>();
        _mapManager = mapManager;
        _serviceProvider = serviceProvider;
        _chunkLayoutFactory = chunkLayoutFactory;
        _proceduralSource = proceduralSource;
        _scopeFactory = scopeFactory;
    }

    public IReadOnlyCollection<IMapInstance> ActiveInstances => _instances.Values.ToList();

    public async Task<IMapInstance> GetOrCreateTownInstanceAsync(MapTemplateId templateId, ushort maxPlayers)
    {
        // Find the least-populated instance that still has room
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

        if (candidate is not null)
        {
            return candidate;
        }

        // All full or none exist — create a new town instance
        _logger.LogInformation("All Town instances for map {TemplateId} are at capacity; creating a new one",
            templateId);
        MapInstance newInstance = await CreateAndInitializeInstanceAsync(templateId, MapType.Town, null);
        return newInstance;
    }

    public async Task<IMapInstance> GetOrCreateNormalInstanceAsync(long accountId, MapTemplateId templateId)
    {
        if (_accountInstanceMap.TryGetValue(accountId, out Dictionary<MapTemplateId, Guid>? accountMap))
        {
            if (accountMap.TryGetValue(templateId, out Guid existingId) &&
                _instances.TryGetValue(existingId, out MapInstance? existing) &&
                !existing.IsExpired(TimeSpan.FromMinutes(15)))
            {
                _logger.LogInformation(
                    "Returning existing Normal instance {InstanceId} for account {AccountId}, map {TemplateId}",
                    existingId, accountId, templateId);
                return existing;
            }
        }

        MapInstance instance = await CreateAndInitializeInstanceAsync(templateId, MapType.Normal, accountId);

        _accountInstanceMap.AddOrUpdate(
            accountId,
            _ => new Dictionary<MapTemplateId, Guid> {{templateId, instance.InstanceId}},
            (_, existing) =>
            {
                existing[templateId] = instance.InstanceId;
                return existing;
            });

        return instance;
    }

    public IMapInstance? GetInstanceById(Guid instanceId) =>
        _instances.TryGetValue(instanceId, out MapInstance? instance) ? instance : null;

    public void RemoveInstance(Guid instanceId)
    {
        if (_instances.TryRemove(instanceId, out _))
        {
            _logger.LogInformation("Instance {InstanceId} removed from registry", instanceId);
        }
    }

    public void ProcessExpiredInstances(TimeSpan normalMapExpiry)
    {
        foreach ((Guid id, MapInstance instance) in _instances)
        {
            if (instance.MapType != MapType.Normal || instance.PlayerCount > 0 ||
                !instance.IsExpired(normalMapExpiry))
            {
                continue;
            }

            if (!_instances.TryRemove(id, out _))
            {
                continue;
            }

            _logger.LogInformation(
                "Normal map instance {InstanceId} for map {TemplateId} freed after expiry",
                id, instance.TemplateId);

            // Clean up account instance map
            if (instance.OwnerAccountId.HasValue &&
                _accountInstanceMap.TryGetValue(instance.OwnerAccountId.Value,
                    out Dictionary<MapTemplateId, Guid>? accountMap))
            {
                accountMap.Remove(instance.TemplateId);
            }
        }
    }

    /// <summary>Registers a pre-built instance (used by World.LoadAsync for town startup instances).</summary>
    public void RegisterInstance(MapInstance instance)
    {
        _instances[instance.InstanceId] = instance;
        _logger.LogInformation("Registered {MapType} instance {InstanceId} for map {TemplateId}",
            instance.MapType, instance.InstanceId, instance.TemplateId);
    }

    /// <summary>Pre-populates the VirtualizedMap cache so on-demand instantiation avoids a file read.</summary>
    public void CacheMapData(MapTemplateId templateId, VirtualizedMap virtualMap) =>
        _virtualMapCache[templateId] = virtualMap;

    private async Task<MapInstance> CreateAndInitializeInstanceAsync(MapTemplateId templateId, MapType mapType,
        long? ownerAccountId, CancellationToken cancellationToken = default)
    {
        MapTemplate template = _mapManager.Templates.FirstOrDefault(t => t.Id == templateId)
                               ?? throw new InvalidOperationException($"MapTemplate {templateId} not found.");

        MapInstance instance;
        if (mapType == MapType.Town)
        {
            instance = await BuildTownInstanceAsync(template, ownerAccountId, cancellationToken);
        }
        else
        {
            instance = await BuildProceduralInstanceAsync(template, ownerAccountId, cancellationToken);
        }

        _instances[instance.InstanceId] = instance;
        _logger.LogInformation("Created {MapType} instance {InstanceId} for map {TemplateId}",
            mapType, instance.InstanceId, templateId);
        return instance;
    }

    private async Task<MapInstance> BuildTownInstanceAsync(MapTemplate template, long? ownerAccountId,
        CancellationToken ct)
    {
        VirtualizedMap virtualMap = await GetOrLoadVirtualMapAsync(template, ct);

        ILoggerFactory loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        IWorld world = _serviceProvider.GetRequiredService<IWorld>();
        IPoolManager poolManager = _serviceProvider.GetRequiredService<IPoolManager>();

        MapInstance instance = new(
            loggerFactory,
            _serviceProvider,
            world,
            poolManager,
            template.Id,
            template.MapType,
            ownerAccountId);

        foreach (MapRegion region in virtualMap.Regions)
        {
            MapNavigator navigator = new(loggerFactory);
            await navigator.LoadAsync(region.MeshFile);
            instance.AddNavigator(region, navigator);
        }

        instance.SpawnStartingEntities();
        return instance;
    }

    private async Task<MapInstance> BuildProceduralInstanceAsync(MapTemplate template, long? ownerAccountId,
        CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IProceduralMapConfigRepository configRepo =
            scope.ServiceProvider.GetRequiredService<IProceduralMapConfigRepository>();
        ProceduralMapConfig config = await configRepo.FindByTemplateIdAsync(template.Id, ct)
            ?? throw new InvalidProceduralConfigException(
                $"No ProceduralMapConfig for map {template.Id.Value}");

        int seed = _proceduralSource.NextSeed();
        return await _chunkLayoutFactory.CreateAsync(template, config, ownerAccountId, seed, ct);
    }

    private async Task<VirtualizedMap> GetOrLoadVirtualMapAsync(MapTemplate template, CancellationToken cancellationToken = default)
    {
        if (_virtualMapCache.TryGetValue(template.Id, out VirtualizedMap? cached))
        {
            return cached;
        }

        VirtualizedMap loaded = await _mapManager.LoadMapDataAsync(template, cancellationToken);
        _virtualMapCache[template.Id] = loaded;
        return loaded;
    }
}
