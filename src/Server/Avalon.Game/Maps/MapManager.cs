using System.Collections.Concurrent;
using Avalon.Database;
using Avalon.Domain.World;
using Avalon.Game.Maps.Virtual;
using Avalon.Game.Pools;
using Microsoft.Extensions.Logging;

namespace Avalon.Game.Maps;

public interface IAvalonMapManager
{
    void LoadMaps();
    MapInstance GenerateInstance(int mapId);
    MapInstance? GetInstance(int mapId, Guid instanceId);
    MapInstance? GetInstance(int mapId, AvalonSession session);
    ConcurrentDictionary<int, ConcurrentDictionary<Guid, MapInstance>> GetInstances();
    MapInstance? AddSessionToMap(int mapId, AvalonSession session, bool initialLoad = false);
    bool RemoveSessionFromMap(AvalonSession session);
}

public class AvalonMapManager : IAvalonMapManager
{
    private readonly ILogger<AvalonMapManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IDatabaseManager _databaseManager;
    private readonly IPoolManager _poolManager;
    private readonly IAvalonSessionManager _sessionManager;

    // MapId, Dictionary<InstanceId, MapInstance>>
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, MapInstance>> _maps = new();

    private readonly ReaderWriterLockSlim _lock;
    
    // List of village map ids, these are special maps that are always loaded and only have a single instance
    private readonly IList<int> _villageMaps = new List<int>();

    // Map template loaded from database
    private IEnumerable<Map>? _mapTemplates;
    
    // Virtual map templates, these are loaded from the map templates and are used to create map instances
    private readonly Dictionary<int, VirtualizedMap> _virtualTemplates = new();

    public AvalonMapManager(ILoggerFactory loggerFactory, IDatabaseManager databaseManager,
        IPoolManager poolManager, IAvalonSessionManager sessionManager)
    {
        _logger = loggerFactory.CreateLogger<AvalonMapManager>();
        _loggerFactory = loggerFactory;
        _databaseManager = databaseManager;
        _poolManager = poolManager;
        _sessionManager = sessionManager;
        _sessionManager.SessionLost += (_, session) => RemoveSessionFromMap(session);
        
        
        _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    }
    
    public async void LoadMaps()
    {
        _logger.LogInformation("Loading maps...");

        _mapTemplates = _databaseManager.World.Map.FindAllAsync().GetAwaiter().GetResult();
        
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
            _maps.TryAdd(map.Id, new ConcurrentDictionary<Guid, MapInstance> { [mapInstance.InstanceId] = mapInstance });
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
                var newInstance = new MapInstance(_mapTemplates!.First(map => map.Id == mapId), _virtualTemplates[mapId]);
                
                // Spawn starting entities
                _poolManager.SpawnStartingEntities(newInstance);
        
                _logger.LogInformation("Map {MapId} '{MapName}' instanced {InstanceId}", newInstance.MapId, newInstance.Name, newInstance.InstanceId);
            
                _maps.TryAdd(mapId, new ConcurrentDictionary<Guid, MapInstance>() { [newInstance.InstanceId] = newInstance});

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

    public MapInstance? GetInstance(int mapId, AvalonSession session)
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

            if (!mapInstances.TryGetValue(Guid.Parse(session.Character!.InstanceId!), out var instance))
            {
                return null;
            }

            if (!instance.ContainsSession(session))
            {
                return null;
            }

            return instance;
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

    public MapInstance? AddSessionToMap(int mapId, AvalonSession session, bool initialLoad = false)
    {
        _lock.EnterWriteLock();
        try
        {
            var mapInstance = GetInstance(mapId, session);
            if (mapInstance != null)
            {
                _logger.LogDebug("Session {SessionId} is already in map {MapId}", session.AccountId, mapId);
                return mapInstance;
            }

            mapInstance = GenerateInstance(mapId);
            
            if (!mapInstance.AddSession(session, initialLoad))
            {
                _logger.LogError("Failed to add session {SessionId} to map {MapId}", session.AccountId, mapId);
                return null;
            }
        
            _logger.LogDebug("Added session {SessionId} to map {MapId}, instance {InstanceId}", session.AccountId, mapId, mapInstance.InstanceId);
            
            return mapInstance;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool RemoveSessionFromMap(AvalonSession session)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!session.InMap)
            {
                _logger.LogWarning("Session {SessionId} is not in a map", session.AccountId);
                return false;
            }
            
            if (!_maps.TryGetValue(session.Character!.Map, out var mapInstances))
            {
                _logger.LogWarning("Map {MapId} not found", session.Character!.Map);
                return false;
            }

            if (!mapInstances.TryGetValue(Guid.Parse(session.Character!.InstanceId!), out var instance))
            {
                _logger.LogWarning("Session {SessionId} is not found in instance {InstanceId} of map {MapId}",
                    session.AccountId, session.Character.InstanceId, session.Character!.Map);
                return false;
            }

            if (!instance.RemoveSession(session))
            {
                _logger.LogWarning("Session {SessionId} could not be removed from instance {InstanceId} of map {MapId}",
                    session.AccountId, session.Character.InstanceId, session.Character!.Map);
                return false;
            }
            
            if (instance.IsEmptySessions() && !_villageMaps.Contains(session.Character!.Map))
            {
                mapInstances.TryRemove(instance.InstanceId, out _);
                _logger.LogInformation("Removed empty instance {InstanceId} from map {MapId} since all sessions left", instance.InstanceId, session.Character!.Map);
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
        if (!_maps.TryGetValue(mapId, out var mapInstances))
        {
            _logger.LogDebug("Map {MapId} not found", mapId);
            return null;
        }

        if (mapInstances.IsEmpty)
        {
            _logger.LogDebug("Map {MapId} has no instances", mapId);
            return null;
        }

        return mapInstances;
    }

}
