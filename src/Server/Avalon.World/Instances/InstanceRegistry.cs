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

    public async Task<IMapInstance> GetOrCreateNormalInstanceAsync(uint characterId, MapTemplateId templateId)
    {
        if (_characterInstanceMap.TryGetValue(characterId, out Dictionary<MapTemplateId, Guid>? characterMap))
        {
            if (characterMap.TryGetValue(templateId, out Guid existingId) &&
                _instances.TryGetValue(existingId, out MapInstance? existing) &&
                !existing.IsExpired(TimeSpan.FromMinutes(15)))
            {
                _logger.LogInformation(
                    "Returning existing Normal instance {InstanceId} for character {CharacterId}, map {TemplateId}",
                    existingId, characterId, templateId);
                return existing;
            }
        }

        MapInstance instance = await CreateAndInitializeInstanceAsync(templateId, MapType.Normal, characterId);

        _characterInstanceMap.AddOrUpdate(
            characterId,
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
