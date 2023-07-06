using System.Collections.Concurrent;
using Avalon.Database;
using Avalon.Database.World;
using Microsoft.Extensions.Logging;

namespace Avalon.Game.Maps;

public interface IAvalonMapManager
{
    Task LoadMaps();
    void Update(TimeSpan deltaTime);

    MapInstance GenerateInstance(int mapId);
    MapInstance? GetInstance(int mapId, Guid instanceId);
    MapInstance? GetInstance(int mapId, int characterId);
    void AddCharacterToMap(int mapId, int characterId);
    bool RemoveCharacterFromMap(int mapId, int characterId);
}

public class AvalonMapManager : IAvalonMapManager
{
    private readonly ILogger<AvalonMapManager> _logger;
    private readonly IDatabaseManager _databaseManager;

    // MapId, Dictionary<InstanceId, MapInstance>>
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, MapInstance>> _instancedMaps = new();
    // Used to lock the map instance when adding/removing characters or doing multiple operations in the same method
    private readonly ConcurrentDictionary<int, object> _mapLocks = new();
    
    private readonly IList<int> _villageMaps = new List<int>();

    private List<Map>? _mapTemplates;
    
    public AvalonMapManager(ILogger<AvalonMapManager> logger, IDatabaseManager databaseManager)
    {
        _logger = logger;
        _databaseManager = databaseManager;
    }
    
    

    public async Task LoadMaps()
    {
        _logger.LogInformation("Loading maps...");

        _mapTemplates = (await _databaseManager.World.Map.QueryAllAsync().ConfigureAwait(true)).ToList();
        
        _logger.LogInformation("Loaded {MapCount} maps from database", _mapTemplates.Count);

        var villageMaps = _mapTemplates.Where(map => map.InstanceType == MapInstanceType.Village).ToArray();
        
        foreach (var map in villageMaps)
        {
            _logger.LogInformation("Loading map {MapId} {MapName}", map.Id, map.Name);
            
            var mapInstance = new MapInstance(map);
            // This can be done the way it is , because we're sure that no other instance of this map exists, since this is server startup logic
            _instancedMaps.TryAdd(map.Id, new ConcurrentDictionary<Guid, MapInstance> { [mapInstance.InstanceId] = mapInstance });
            _villageMaps.Add(map.Id);
            
            _logger.LogInformation("Map {MapId} '{MapName}' instanced {InstanceId}", mapInstance.MapId, mapInstance.Name, mapInstance.InstanceId);
        }
    }

    public void Update(TimeSpan deltaTime)
    {
        foreach (var (mapId, mapInstances) in _instancedMaps)
        {
            foreach (var (instanceId, mapInstance) in mapInstances)
            {
                mapInstance.Update(deltaTime);
            }
        }
    }
    
    public MapInstance GenerateInstance(int mapId)
    {
        if (!_instancedMaps.TryGetValue(mapId, out var mapInstances))
        {
            var newInstance = new MapInstance(_mapTemplates!.First(map => map.Id == mapId));
        
            _logger.LogInformation("Map {MapId} '{MapName}' instanced {InstanceId}", newInstance.MapId, newInstance.Name, newInstance.InstanceId);
            
            _instancedMaps.TryAdd(mapId, new ConcurrentDictionary<Guid, MapInstance>() { [newInstance.InstanceId] = newInstance});

            return newInstance;
        }
        
        if (_villageMaps.Contains(mapId))
        {
            // do not generate a new instance for village maps
            return mapInstances.First().Value;
        }

        var mapInstance = new MapInstance(_mapTemplates!.First(map => map.Id == mapId));
        
        _logger.LogInformation("Map {MapId} '{MapName}' instanced {InstanceId}", mapInstance.MapId, mapInstance.Name, mapInstance.InstanceId);
        
        mapInstances.TryAdd(mapInstance.InstanceId, mapInstance);

        return mapInstance;
    }

    public MapInstance? GetInstance(int mapId, int characterId)
    {
        var mapInstances = GetMapInstances(mapId);
        if (mapInstances == null) return null;
        
        // There are some reserved maps ids that have a single instance, so we can just return that
        if (_villageMaps.Contains(mapId))
        {
            return mapInstances.First().Value;
        }

        foreach (var (instanceId, instance) in mapInstances)
        {
            if (instance.ContainsCharacter(characterId))
            {
                return instance;
            }
        }
    
        return null;
    }

    public MapInstance? GetInstance(int mapId, Guid instanceId)
    {
        var mapInstances = GetMapInstances(mapId);
        if (mapInstances == null) return null;
        
        if (_villageMaps.Contains(mapId))
        {
            return mapInstances.First().Value;
        }

        mapInstances.TryGetValue(instanceId, out var instance);
        return instance;
    }
    
    public void AddCharacterToMap(int mapId, int characterId)
    {
        var mapInstance = GetInstance(mapId, characterId);
        if (mapInstance != null)
        {
            _logger.LogDebug("Character {CharacterId} is already in map {MapId}", characterId, mapId);
            return;
        }

        mapInstance = GenerateInstance(mapId);
        mapInstance.AddCharacter(characterId);
        
        _logger.LogDebug("Added character {CharacterId} to map {MapId}, instance {InstanceId}", characterId, mapId, mapInstance.InstanceId);
    }
    
    public bool RemoveCharacterFromMap(int mapId, int characterId)
    {
        if (!_instancedMaps.TryGetValue(mapId, out var mapInstances))
        {
            _logger.LogWarning("Map {MapId} not found", mapId);
            return false;
        }

        Guid? foundId = null;

        lock (_mapLocks.GetOrAdd(mapId, new object()))
        {
            foreach (var (id, instance) in mapInstances)
            {
                if (!instance.ContainsCharacter(characterId)) continue;
            
                instance.RemoveCharacter(characterId);
                
                _logger.LogInformation("Removed character {CharacterId} from map {MapId}, instance {InstanceId}", characterId, mapId, instance.InstanceId);
                
                if (instance.IsEmptyCharacters() && !_villageMaps.Contains(mapId))
                {
                    foundId = id;
                }
                break;
            }
        
            if (foundId.HasValue)
            {
                mapInstances.TryRemove(foundId.Value, out _);
                _logger.LogInformation("Removed empty instance {InstanceId} from map {MapId} since all characters left", foundId.Value, mapId);
            }
        }

        return true;
    }

    private bool RemoveInstance(int mapId, Guid instanceId)
    {
        if (!_instancedMaps.TryGetValue(mapId, out var mapInstances))
        {
            _logger.LogWarning("Map {MapId} not found", mapId);
            return false;
        }

        return mapInstances.TryRemove(instanceId, out _);
    }
    
    
    
    private ConcurrentDictionary<Guid, MapInstance>? GetMapInstances(int mapId)
    {
        if (!_instancedMaps.TryGetValue(mapId, out var mapInstances))
        {
            _logger.LogWarning("Map {MapId} not found", mapId);
            return null;
        }

        if (mapInstances.IsEmpty)
        {
            _logger.LogWarning("Map {MapId} has no instances", mapId);
            return null;
        }

        return mapInstances;
    }

}
