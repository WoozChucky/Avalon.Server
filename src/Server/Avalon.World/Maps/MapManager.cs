using System.Collections.Concurrent;
using Avalon.Domain.World;
using Avalon.Game;
using Avalon.Game.Configuration;
using Avalon.World.Database.Repositories;
using Avalon.World.Maps.Virtual;
using Avalon.World.Pools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.World.Maps;

public interface IAvalonMapManager
{
    Task LoadAsync();
    MapInstance GenerateInstance(int mapId);
    MapInstance? GetInstance(int mapId, Guid instanceId);
    MapInstance? GetInstance(int mapId, IWorldConnection connection);
    ConcurrentDictionary<int, IList<MapInstance>> GetInstances();
    MapInstance? AddConnectionToMap(int mapId, IWorldConnection connection, bool initialLoad = false);
    bool RemoveSessionFromMap(IWorldConnection connection);
    Task Update(TimeSpan deltaTime);
}

public class AvalonMapManager : IAvalonMapManager
{
    private readonly ILogger<AvalonMapManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IPoolManager _poolManager;
    private readonly GameConfiguration _gameConfiguration;
    private readonly IMapTemplateRepository _mapTemplateRepository;

    // MapId, Dictionary<InstanceId, MapInstance>>
    private readonly ConcurrentDictionary<int, IList<MapInstance>> _maps = new();

    private readonly ReaderWriterLockSlim _lock;
    
    // List of village map ids, these are special maps that are always loaded and only have a single instance
    private readonly IList<int> _villageMaps = new List<int>();

    // Map template loaded from database
    private IEnumerable<MapTemplate>? _mapTemplates;
    
    // Virtual map templates, these are loaded from the map templates and are used to create map instances
    private readonly Dictionary<int, VirtualizedMap> _virtualTemplates = new();

    public AvalonMapManager(ILoggerFactory loggerFactory, IPoolManager poolManager,
        IOptions<GameConfiguration> gameConfiguration, IMapTemplateRepository mapTemplateRepository)
    {
        _logger = loggerFactory.CreateLogger<AvalonMapManager>();
        _loggerFactory = loggerFactory;
        _poolManager = poolManager;
        _gameConfiguration = gameConfiguration.Value;
        _mapTemplateRepository = mapTemplateRepository;
        
        _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    }
    
    public async Task LoadAsync()
    {
        _logger.LogInformation("Loading maps...");

        _mapTemplates = await _mapTemplateRepository.FindAllAsync();
        
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
            
            var mapInstance = new MapInstance(_loggerFactory, map, _virtualTemplates[map.Id], _gameConfiguration);
            
            // Spawn starting entities
            _poolManager.SpawnStartingEntities(mapInstance);
            
            // This can be done the way it is , because we're sure that no other instance of this map exists, since this is server startup logic
            _maps.TryAdd(map.Id, [mapInstance]);
            _villageMaps.Add(map.Id);
            
            _logger.LogInformation("Map {MapId} '{MapName}' instanced {InstanceId}", mapInstance.MapId, mapInstance.Name, mapInstance.InstanceId);
        }
    }

    public MapInstance GenerateInstance(int mapId)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_maps.TryGetValue(mapId, out var mapInstances))
            {
                // if no instances exist for this map, create the first one
                var newInstance = new MapInstance(_loggerFactory, _mapTemplates!.First(map => map.Id == mapId), _virtualTemplates[mapId], _gameConfiguration);
                
                // Spawn starting entities
                _poolManager.SpawnStartingEntities(newInstance);
        
                _logger.LogInformation("Map {MapId} '{MapName}' instanced {InstanceId}", newInstance.MapId, newInstance.Name, newInstance.InstanceId);
            
                _maps.TryAdd(mapId, [newInstance]);

                return newInstance;
            }
        
            if (_villageMaps.Contains(mapId))
            {
                // do not generate a new instance for village maps, those are made sure to only have a single instance
                return mapInstances.First();
            }

            var mapInstance = new MapInstance(_loggerFactory, _mapTemplates!.First(map => map.Id == mapId), _virtualTemplates[mapId], _gameConfiguration);
            
            // Spawn starting entities
            _poolManager.SpawnStartingEntities(mapInstance);
        
            _logger.LogInformation("Map {MapId} '{MapName}' instanced {InstanceId}", mapInstance.MapId, mapInstance.Name, mapInstance.InstanceId);
        
            mapInstances.Add(mapInstance);

            return mapInstance;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public MapInstance? GetInstance(int mapId, IWorldConnection connection)
    {
        _lock.EnterReadLock();
        try
        {
            var mapInstances = GetMapInstances(mapId);
            if (mapInstances == null) return null;
        
            // There are some reserved maps ids that have a single instance, so we can just return that
            if (_villageMaps.Contains(mapId))
            {
                return mapInstances.First();
            }
            
            if (mapInstances.Count == 0)
            {
                return null;
            }
            
            var instance = mapInstances.FirstOrDefault(map => map.ContainsConnection(connection));
            if (instance == null)
            {
                _logger.LogWarning("Session {SessionId} not found in map {MapId}", connection.AccountId, mapId);
                return null;
            }

            return instance;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public ConcurrentDictionary<int, IList<MapInstance>> GetInstances()
    {
        _lock.EnterReadLock();
        try
        {
            return _maps;
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
            if (mapInstances == null || mapInstances.Count == 0) return null;
        
            if (_villageMaps.Contains(mapId))
            {
                return mapInstances.First();
            }

            var instance = mapInstances.FirstOrDefault(map => map.InstanceId == instanceId);
            return instance;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public MapInstance? AddConnectionToMap(int mapId, IWorldConnection connection, bool initialLoad = false)
    {
        _lock.EnterWriteLock();
        try
        {
            var mapInstance = GetInstance(mapId, connection);
            if (mapInstance != null)
            {
                _logger.LogDebug("Session {SessionId} is already in map {MapId}", connection.AccountId, mapId);
                return mapInstance;
            }

            mapInstance = GenerateInstance(mapId);
            
            if (!mapInstance.AddConnection(connection, initialLoad))
            {
                _logger.LogError("Failed to add session {SessionId} to map {MapId}", connection.AccountId, mapId);
                return null;
            }
        
            _logger.LogDebug("Added session {SessionId} to map {MapId}, instance {InstanceId}", connection.AccountId, mapId, mapInstance.InstanceId);
            
            return mapInstance;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool RemoveSessionFromMap(IWorldConnection connection)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!connection.InMap)
            {
                _logger.LogWarning("Session {SessionId} is not in a map", connection.AccountId);
                return false;
            }
            
            if (!_maps.TryGetValue(connection.Character!.Map, out var mapInstances))
            {
                _logger.LogWarning("Map {MapId} not found", connection.Character!.Map);
                return false;
            }

            var instance = mapInstances.FirstOrDefault(map => map.InstanceId == Guid.Parse(connection.Character!.InstanceId!));
            if (instance == null)
            {
                _logger.LogWarning("Session {SessionId} is not found in instance {InstanceId} of map {MapId}",
                    connection.AccountId, connection.Character.InstanceId, connection.Character!.Map);
                return false;
            }

            if (!instance.RemoveSession(connection))
            {
                _logger.LogWarning("Session {SessionId} could not be removed from instance {InstanceId} of map {MapId}",
                    connection.AccountId, connection.Character.InstanceId, connection.Character!.Map);
                return false;
            }
            
            instance.OnConnectionLeft(connection);
            
            if (instance.IsEmpty && !_villageMaps.Contains(connection.Character!.Map))
            {
                _logger.LogInformation("Removed empty instance {InstanceId} from map {MapId} since all sessions left", instance.InstanceId, connection.Character!.Map);
                return mapInstances.Remove(instance);
            }
        
            return true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public async Task Update(TimeSpan deltaTime)
    {
        //_lock.EnterReadLock();
        try
        {
            var maps = _maps.Values.SelectMany(m => m).ToList();
            
            var updateTasks = maps.Select(mapInstance => mapInstance.Update(deltaTime));
            
            await Task.WhenAll(updateTasks);
        }
        finally
        {
            //_lock.ExitReadLock();
        }
    }

    private IList<MapInstance>? GetMapInstances(int mapId)
    {
        if (!_maps.TryGetValue(mapId, out var mapInstances))
        {
            _logger.LogDebug("Map {MapId} not found", mapId);
            return null;
        }

        if (mapInstances.Count == 0)
        {
            _logger.LogDebug("Map {MapId} has no instances", mapId);
            return null;
        }

        return mapInstances;
    }

}
