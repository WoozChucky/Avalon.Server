using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalon.Database;
using Avalon.Database.World;
using Avalon.Database.World.Model;
using Avalon.Game.Maps.Virtual;
using Avalon.Game.Pools;
using Microsoft.Extensions.Logging;

namespace Avalon.Game.Maps;

public interface IAvalonMapManager
{
    void LoadMaps();
    MapInstance GenerateInstance(int mapId);
    MapInstance? GetInstance(int mapId, Guid instanceId);
    MapInstance? GetInstance(int mapId, int characterId);
    ConcurrentDictionary<int, ConcurrentDictionary<Guid, MapInstance>> GetInstances();
    MapInstance AddCharacterToMap(int mapId, int characterId);
    bool RemoveCharacterFromMap(int mapId, int characterId);
}

public class AvalonMapManager : IAvalonMapManager
{
    private readonly ILogger<AvalonMapManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IDatabaseManager _databaseManager;
    private readonly IPoolManager _poolManager;

    // MapId, Dictionary<InstanceId, MapInstance>>
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, MapInstance>> _instancedMaps = new();

    private readonly ReaderWriterLockSlim _lock;
    
    // List of village map ids, these are special maps that are always loaded and only have a single instance
    private readonly IList<int> _villageMaps = new List<int>();

    // Map template loaded from database
    private IEnumerable<Map>? _mapTemplates;
    
    // Virtual map templates, these are loaded from the map templates and are used to create map instances
    private readonly Dictionary<int, VirtualizedMap> _virtualTemplates = new();

    public AvalonMapManager(ILoggerFactory loggerFactory, IDatabaseManager databaseManager,
        IPoolManager poolManager)
    {
        _logger = loggerFactory.CreateLogger<AvalonMapManager>();
        _loggerFactory = loggerFactory;
        _databaseManager = databaseManager;
        _poolManager = poolManager;
        _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    }
    
    public async void LoadMaps()
    {
        _logger.LogInformation("Loading maps...");

        _mapTemplates = _databaseManager.World.Map.QueryAllAsync().GetAwaiter().GetResult();
        
        _logger.LogInformation("Loaded {MapCount} maps from database", _mapTemplates.Count());

        var villageMaps = _mapTemplates.Where(map => map.InstanceType == MapInstanceType.Village).ToArray();

        foreach (var mapTemplate in _mapTemplates)
        {
            // Preload the virtual map templates
            _logger.LogInformation("PreLoading virtual map {MapId} - {MapName} - {MapDescription}", mapTemplate.Id, mapTemplate.Name, mapTemplate.Description);
            _virtualTemplates.Add(mapTemplate.Id, new VirtualizedMap(_loggerFactory, mapTemplate.Id, mapTemplate.Name, mapTemplate.Directory));
        }
        
        foreach (var map in villageMaps)
        {
            _logger.LogInformation("Initializing instance map {MapId} {MapName}", map.Id, map.Name);
            
            var mapInstance = new MapInstance(map, _virtualTemplates[map.Id]);
            
            // Spawn starting entities
            _poolManager.SpawnStartingEntities(mapInstance);
            
            // This can be done the way it is , because we're sure that no other instance of this map exists, since this is server startup logic
            _instancedMaps.TryAdd(map.Id, new ConcurrentDictionary<Guid, MapInstance> { [mapInstance.InstanceId] = mapInstance });
            _villageMaps.Add(map.Id);
            
            _logger.LogInformation("Map {MapId} '{MapName}' instanced {InstanceId}", mapInstance.MapId, mapInstance.Name, mapInstance.InstanceId);
        }
    }

    public MapInstance GenerateInstance(int mapId)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_instancedMaps.TryGetValue(mapId, out var mapInstances))
            {
                // if no instances exist for this map, create the first one
                var newInstance = new MapInstance(_mapTemplates!.First(map => map.Id == mapId), _virtualTemplates[mapId]);
                
                // Spawn starting entities
                _poolManager.SpawnStartingEntities(newInstance);
        
                _logger.LogInformation("Map {MapId} '{MapName}' instanced {InstanceId}", newInstance.MapId, newInstance.Name, newInstance.InstanceId);
            
                _instancedMaps.TryAdd(mapId, new ConcurrentDictionary<Guid, MapInstance>() { [newInstance.InstanceId] = newInstance});

                return newInstance;
            }
        
            if (_villageMaps.Contains(mapId))
            {
                // do not generate a new instance for village maps, those are made sure to only have a single instance
                return mapInstances.First().Value;
            }

            var mapInstance = new MapInstance(_mapTemplates!.First(map => map.Id == mapId), _virtualTemplates[mapId]);
            
            // Spawn starting entities
            _poolManager.SpawnStartingEntities(mapInstance);
        
            _logger.LogInformation("Map {MapId} '{MapName}' instanced {InstanceId}", mapInstance.MapId, mapInstance.Name, mapInstance.InstanceId);
        
            mapInstances.TryAdd(mapInstance.InstanceId, mapInstance);

            return mapInstance;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public MapInstance? GetInstance(int mapId, int characterId)
    {
        _lock.EnterReadLock();
        try
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
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public ConcurrentDictionary<int, ConcurrentDictionary<Guid, MapInstance>> GetInstances()
    {
        _lock.EnterReadLock();
        try
        {
            return _instancedMaps;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public MapInstance? GetInstance(int mapId, Guid instanceId)
    {
        _lock.EnterReadLock();
        try
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
        finally
        {
            _lock.ExitReadLock();
        }
    }
    
    public MapInstance AddCharacterToMap(int mapId, int characterId)
    {
        _lock.EnterWriteLock();
        try
        {
            var mapInstance = GetInstance(mapId, characterId);
            if (mapInstance != null)
            {
                _logger.LogDebug("Character {CharacterId} is already in map {MapId}", characterId, mapId);
                return mapInstance;
            }

            mapInstance = GenerateInstance(mapId);
            mapInstance.AddCharacter(characterId);
        
            _logger.LogDebug("Added character {CharacterId} to map {MapId}, instance {InstanceId}", characterId, mapId, mapInstance.InstanceId);
            
            return mapInstance;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
    
    public bool RemoveCharacterFromMap(int mapId, int characterId)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_instancedMaps.TryGetValue(mapId, out var mapInstances))
            {
                _logger.LogWarning("Map {MapId} not found", mapId);
                return false;
            }

            Guid? foundId = null;

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
        
            return true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
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
