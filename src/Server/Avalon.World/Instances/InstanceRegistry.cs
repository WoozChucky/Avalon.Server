using System.Collections.Concurrent;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Maps;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Instances;

public class InstanceRegistry : IInstanceRegistry
{
    // characterId → { templateId → instanceId } for Normal map re-entry. Keyed by character
    // (not account) so different characters on the same account land in different instances
    // even within the 15-min re-entry window.
    private readonly ConcurrentDictionary<uint, Dictionary<MapTemplateId, Guid>> _characterInstanceMap = new();
    private readonly ConcurrentDictionary<Guid, MapInstance> _instances = new();

    // Town builds still running, one per map. A build bakes a navmesh and reads the database, and
    // the instance is registered in _instances only once it finishes — so without this, a second
    // arrival during that window finds no town with room and starts a second copy (#442). Lazy so
    // that two callers racing GetOrAdd cannot both start a build.
    private readonly ConcurrentDictionary<MapTemplateId, Lazy<Task<MapInstance>>> _pendingTownBuilds = new();

    // The same, per character, for Normal maps: a portal request sent twice before the first build
    // finishes would otherwise bake two navmeshes and orphan the first instance until it expires.
    private readonly ConcurrentDictionary<(uint CharacterId, MapTemplateId TemplateId), Lazy<Task<MapInstance>>>
        _pendingNormalBuilds = new();

    private readonly ILogger<InstanceRegistry> _logger;
    private readonly IAvalonMapManager _mapManager;
    private readonly IChunkLayoutInstanceFactory _chunkLayoutFactory;

    public InstanceRegistry(
        ILoggerFactory loggerFactory,
        IAvalonMapManager mapManager,
        IChunkLayoutInstanceFactory chunkLayoutFactory)
    {
        _logger = loggerFactory.CreateLogger<InstanceRegistry>();
        _mapManager = mapManager;
        _chunkLayoutFactory = chunkLayoutFactory;
    }

    public IReadOnlyCollection<IMapInstance> ActiveInstances => _instances.Values.ToList();

    public async Task<IMapInstance> GetOrCreateTownInstanceAsync(MapTemplateId templateId, ushort maxPlayers)
    {
        MapInstance? candidate = FindTownWithRoom(templateId, maxPlayers);
        if (candidate is not null)
        {
            return candidate;
        }

        Lazy<Task<MapInstance>> build = _pendingTownBuilds.GetOrAdd(templateId,
            id => new Lazy<Task<MapInstance>>(() => BuildTownAsync(id, maxPlayers)));

        return await build.Value;
    }

    private async Task<MapInstance> BuildTownAsync(MapTemplateId templateId, ushort maxPlayers)
    {
        try
        {
            // A build that finished between the caller's scan and its GetOrAdd has already
            // registered its town and removed its pending entry — registration comes first — so
            // looking again here is what stops that caller starting a second copy.
            MapInstance? finished = FindTownWithRoom(templateId, maxPlayers);
            if (finished is not null)
            {
                return finished;
            }

            _logger.LogInformation("All Town instances for map {TemplateId} are at capacity; creating a new one",
                templateId);
            return await CreateAndInitializeInstanceAsync(templateId, MapType.Town, null);
        }
        finally
        {
            // Success or failure, the next arrival must see either the registered town or nothing:
            // a failed build left here would hand its exception to every later caller.
            _pendingTownBuilds.TryRemove(templateId, out _);
        }
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

    public async Task<IMapInstance> GetOrCreateNormalInstanceAsync(uint characterId, MapTemplateId templateId)
    {
        MapInstance? existing = FindReentryInstance(characterId, templateId);
        if (existing is not null)
        {
            return existing;
        }

        Lazy<Task<MapInstance>> build = _pendingNormalBuilds.GetOrAdd((characterId, templateId),
            key => new Lazy<Task<MapInstance>>(() => BuildNormalAsync(key.CharacterId, key.TemplateId)));

        return await build.Value;
    }

    private async Task<MapInstance> BuildNormalAsync(uint characterId, MapTemplateId templateId)
    {
        try
        {
            // As for towns: a build that finished between the caller's check and its GetOrAdd has
            // already recorded its instance for this character, because that happens before the
            // pending entry is removed.
            MapInstance? finished = FindReentryInstance(characterId, templateId);
            if (finished is not null)
            {
                return finished;
            }

            MapInstance instance = await CreateAndInitializeInstanceAsync(templateId, MapType.Normal, characterId);

            _characterInstanceMap.AddOrUpdate(
                characterId,
                _ => new Dictionary<MapTemplateId, Guid> {{templateId, instance.InstanceId}},
                (_, map) =>
                {
                    map[templateId] = instance.InstanceId;
                    return map;
                });

            return instance;
        }
        finally
        {
            _pendingNormalBuilds.TryRemove((characterId, templateId), out _);
        }
    }

    /// <summary>The character's unexpired instance of this map, if it still has one.</summary>
    private MapInstance? FindReentryInstance(uint characterId, MapTemplateId templateId)
    {
        if (_characterInstanceMap.TryGetValue(characterId, out Dictionary<MapTemplateId, Guid>? characterMap) &&
            characterMap.TryGetValue(templateId, out Guid existingId) &&
            _instances.TryGetValue(existingId, out MapInstance? existing) &&
            !existing.IsExpired(TimeSpan.FromMinutes(15)))
        {
            _logger.LogInformation(
                "Returning existing Normal instance {InstanceId} for character {CharacterId}, map {TemplateId}",
                existingId, characterId, templateId);
            return existing;
        }

        return null;
    }

    public IMapInstance? GetInstanceById(Guid instanceId) =>
        _instances.TryGetValue(instanceId, out MapInstance? instance) ? instance : null;

    public void RemoveInstance(Guid instanceId)
    {
        // Dispose, not just drop: MapInstance subscribes to static entity events, so an instance that
        // is only removed from this dictionary stays reachable through those delegates and is never
        // collected.
        if (_instances.TryRemove(instanceId, out MapInstance? instance))
        {
            instance.Dispose();
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

            if (!_instances.TryRemove(id, out MapInstance? removed))
            {
                continue;
            }

            // Without this the log line below is untrue: the instance stays rooted by the static
            // entity events it subscribed to in its constructor.
            removed.Dispose();

            _logger.LogInformation(
                "Normal map instance {InstanceId} for map {TemplateId} freed after expiry",
                id, instance.TemplateId);

            // Clean up character instance map
            if (instance.OwnerCharacterId.HasValue &&
                _characterInstanceMap.TryGetValue(instance.OwnerCharacterId.Value,
                    out Dictionary<MapTemplateId, Guid>? characterMap))
            {
                characterMap.Remove(instance.TemplateId);
            }
        }
    }

    private async Task<MapInstance> CreateAndInitializeInstanceAsync(MapTemplateId templateId, MapType mapType,
        uint? ownerCharacterId, CancellationToken cancellationToken = default)
    {
        MapTemplate template = _mapManager.Templates.FirstOrDefault(t => t.Id == templateId)
                               ?? throw new InvalidOperationException($"MapTemplate {templateId} not found.");

        MapInstance instance = await _chunkLayoutFactory.BuildAsync(template, ownerCharacterId, cancellationToken);

        _instances[instance.InstanceId] = instance;
        _logger.LogInformation("Created {MapType} instance {InstanceId} for map {TemplateId}",
            mapType, instance.InstanceId, templateId);
        return instance;
    }
}
