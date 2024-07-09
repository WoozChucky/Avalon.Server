using System.Drawing;
using Avalon.Auth.Database.Repositories;
using Avalon.Common.Mathematics;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.World;
using Avalon.Game.Configuration;
using Avalon.Network.Packets.Movement;
using Avalon.World.Entities;
using Avalon.World.Maps;
using Avalon.World.Maps.Virtualized;
using Avalon.World.Pools;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.World;

public interface IWorld
{
    WorldId Id { get; }
    string MinVersion { get; }
    string CurrentVersion { get; }
    GameConfiguration Configuration { get; }

    Task Update(TimeSpan deltaTime);
    Task SpawnPlayerAsync(IWorldConnection connection);
    Task DespawnPlayerAsync(IWorldConnection connection);
}

public class World : IWorld
{
    public WorldId Id => Configuration.WorldId;
    public string MinVersion => _world?.MinVersion ?? throw new InvalidOperationException("World not loaded.");
    public string CurrentVersion => _world?.Version ?? throw new InvalidOperationException("World not loaded.");
    public GameConfiguration Configuration => _configuration.Value;

    private Avalon.Domain.Auth.World? _world;
    private readonly ILogger<World> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptions<GameConfiguration> _configuration;
    private readonly IWorldRepository _worldRepository;
    private readonly IAvalonMapManager _mapManager;
    private readonly IPoolManager _poolManager;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public World(ILoggerFactory loggerFactory,
        IOptions<GameConfiguration> configuration, 
        IWorldRepository worldRepository, 
        IAvalonMapManager mapManager,
        IPoolManager poolManager,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = loggerFactory.CreateLogger<World>();
        _loggerFactory = loggerFactory;
        _configuration = configuration;
        _worldRepository = worldRepository;
        _mapManager = mapManager;
        _poolManager = poolManager;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public WorldGrid Grid { get; private set; }

    public async Task LoadAsync(CancellationToken token)
    {
        var world = await _worldRepository.FindByIdAsync(Id);

        _world = world ?? throw new InvalidOperationException($"World {Id} not found.");

        await _mapManager.LoadAsync();

        Grid = new WorldGrid();

        var chunkId = 0U;
        
        await foreach (var (virtualMap, mapTemplate) in _mapManager.EnumerateOpenWorldAsync(token))
        {
            var chunksMetadata = virtualMap.Chunks;

            var chunks = new Dictionary<Vector2, Chunk>();
            
            foreach (var chunkMetadata in chunksMetadata)
            {
                var chunk = new Chunk(_loggerFactory)
                {
                    Id = chunkId++,
                    Metadata = chunkMetadata,
                    Neighbors = [] // TODO: Fill after loading all chunks
                };
                
                var key = new Vector2(chunkMetadata.Position.x, chunkMetadata.Position.z);
                chunks[key] = chunk;
            }
            
            var map = new Map(_loggerFactory)
            {
                Id = mapTemplate.Id,
                Metadata = mapTemplate,
                Size = virtualMap.Size,
                Chunks = chunks
            };
            
            Grid.AddMap(map);
        }
        
        Grid.DetectNeighbors();
        
        Grid.SpawnStartingEntities(_poolManager);
        
        // var path = AStarPathfinding.GeneratePathSync(new Vector3(3, 100, 13), new Vector3(26, 100, 39), chunky!.NavMesh.Indices, chunky!.NavMesh.Vertices, chunky!.NavMesh.Areas);
    }
    
    public async Task Update(TimeSpan deltaTime)
    {
        var tasks = Grid.Maps
            .AsParallel()                                     // Process maps in parallel
            .SelectMany(map => map.Chunks.Values.AsParallel() // Process chunks in parallel within each map
                .Where(chunk => chunk.Enabled)                // Only process enabled chunks
                .Select(chunk => chunk.Update(deltaTime)))
            .ToList();

        await Task.WhenAll(tasks);
    }

    public Task SpawnPlayerAsync(IWorldConnection connection)
    {
        Grid.AddPlayer(connection);
        return Task.CompletedTask;
    }

    public async Task DespawnPlayerAsync(IWorldConnection connection)
    {
        _logger.LogDebug("Was called!");

        try
        {
            Grid.RemovePlayer(connection);

            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var characterRepository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();

            // This probably should be converted to a logout function
            connection.Character!.X = connection.Character.Movement.Position.x;
            connection.Character.Y = connection.Character.Movement.Position.y;
            connection.Character.Z = connection.Character.Movement.Position.z;
            connection.Character.Online = false;
            connection.Character.LevelTime +=
                (ulong) (DateTime.UtcNow - connection.Character.EnteredWorld).TotalSeconds;
            connection.Character.TotalTime +=
                (ulong) (DateTime.UtcNow - connection.Character.EnteredWorld).TotalSeconds;
            await characterRepository.UpdateAsync(connection.Character!);
        }
        catch (InvalidOperationException) { } // Ignore if character is not found
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to save character {CharacterId} on world despawn", connection.Character!.Id);
        }
    }
}

public class WorldGrid
{
    public IList<Map> Maps
    {
        get
        {
            lock (_mapsLock)
            {
                return _maps;
            }
        }
    }

    private readonly IList<Map> _maps = [];
    private readonly object _mapsLock = new object();

    public void AddPlayer(IWorldConnection connection)
    {
        if (connection.Character == null)
        {
            throw new InvalidOperationException("Character not found in connection");
        }
        
        var mapId = connection.Character.Map;
        
        Map? map;
        lock (_mapsLock)
        {
            map = _maps.FirstOrDefault(m => m.Id == mapId);
        }
        
        if (map == null)
        {
            throw new InvalidOperationException($"Map {mapId} not found");
        }
        
        map.AddPlayer(connection);
    }

    public void RemovePlayer(IWorldConnection connection)
    {
        if (connection.Character == null)
        {
            throw new InvalidOperationException("Character not found in connection");
        }
        
        var mapId = connection.Character.Map;
        
        Map? map;
        lock (_mapsLock)
        {
            map = _maps.FirstOrDefault(m => m.Id == mapId);
        }
        
        if (map == null)
        {
            throw new InvalidOperationException($"Map {mapId} not found");
        }
        
        map.RemovePlayer(connection);
    }

    public void AddMap(Map map)
    {
        lock (_mapsLock)
        {
            _maps.Add(map);
        }
    }

    public void DetectNeighbors()
    {
        foreach (var map in Maps)
        {
            map.DetectNeighbors();
        }
    }

    public void SpawnStartingEntities(IPoolManager poolManager)
    {
        foreach (var map in Maps)
        {
            map.SpawnStartingEntities(poolManager);
        }
    }
}

public class Map
{
    public ushort Id { get; set; }
    public MapTemplate Metadata { get; set; }
    public Vector3 Size { get; set; }
    public Dictionary<Vector2, Chunk> Chunks { get; set; }
    
    private readonly ILogger<Map> _logger;
    
    private IPoolManager _poolManager;
    
    public Map(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<Map>();
    }

    public void AddPlayer(IWorldConnection connection)
    {
        if (connection.Character == null)
        {
            throw new InvalidOperationException("Character not found in connection");
        }
        
        var position = connection.Character.Movement.Position;
        
        var chunk = GetChunk(position);
        if (chunk == null)
        {
            throw new InvalidOperationException($"Chunk not found for position {position}");
        }
        
        chunk.AddPlayer(connection);
        _logger.LogInformation("Player {CharacterId} added to chunk {ChunkId} of map {MapId}", connection.Character.Name, chunk.Id, Id);
    }

    public void RemovePlayer(IWorldConnection connection)
    {
        if (connection.Character == null)
        {
            throw new InvalidOperationException("Character not found in connection");
        }
        
        var position = connection.Character.Movement.Position;
        
        var chunk = GetChunk(position);
        if (chunk == null)
        {
            throw new InvalidOperationException($"Chunk not found for position {position}");
        }
        
        chunk.RemovePlayer(connection);
        _logger.LogInformation("Player {CharacterId} removed from chunk {ChunkId} of map {MapId}", connection.Character.Name, chunk.Id, Id);
    }
    
    public void DetectNeighbors()
    {
        foreach (var chunk in Chunks.Values)
        {
            // Find the neighbors of the chunk
            var neighbors = new List<Chunk>();
            foreach (var otherChunk in Chunks.Values)
            {
                if (chunk == otherChunk) continue;

                // Calculate the distance between chunks in the X and Z directions
                var deltaX = Mathf.Abs(chunk.Metadata.Position.x - otherChunk.Metadata.Position.x);
                var deltaZ = Mathf.Abs(chunk.Metadata.Position.z - otherChunk.Metadata.Position.z);

                // Check if the chunks are adjacent (including diagonally)
                if (Mathf.Approximately(deltaX, chunk.Metadata.Size.x) && deltaZ <= chunk.Metadata.Size.z)
                {
                    neighbors.Add(otherChunk);
                }
                else if (Mathf.Approximately(deltaZ, chunk.Metadata.Size.z) && deltaX <= chunk.Metadata.Size.x)
                {
                    neighbors.Add(otherChunk);
                }
            }
            chunk.Neighbors = neighbors;
        }
    }
    
    public Chunk? GetChunk(Vector3 position)
    {
        foreach (var key in Chunks.Keys)
        {
            var chunk = Chunks[key];
            // Calculate the chunk bounds
            var min = chunk.Metadata.Position;
            var max = chunk.Metadata.Position + new Vector3(chunk.Metadata.Size.x, 0, chunk.Metadata.Size.z);
            // Check if the position is within the bounds of this chunk
            if (position.x >= min.x && position.x < max.x &&
                position.z >= min.z && position.z < max.z)
            {
                return chunk;
            }
        }
        return null;
    }

    public void SpawnStartingEntities(IPoolManager poolManager)
    {
        foreach (var chunk in Chunks)
        {
            chunk.Value.SpawnStartingEntities(poolManager);
        }
    }
}

public class Chunk
{
    public uint Id;
    public bool Enabled { get; set; }
    public required ChunkMetadata Metadata { get; init; }
    public List<Chunk> Neighbors { get; set; } = [];
    
    private readonly List<Creature> _creatures = [];
    private readonly List<IWorldConnection> _connections = [];
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly ILogger<Chunk> _logger;
    
    private const float BroadcastInterval = 0.1f;
    
    private IPoolManager _poolManager;
    private float _lastBroadcastTime;

    public Chunk(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<Chunk>();
    }

    public async Task Update(TimeSpan deltaTime)
    {
        if (!Enabled) return;
        
        var connectionsSnapshot = GetConnections();
        var creaturesSnapshot = GetCreatures();
        
        /*
        _lock.EnterReadLock();
        try
        {*/
            var creatureUpdates = _creatures.Where(c => c.Script != null).Select(c => c.Script!.Update(deltaTime));
        
            await Task.WhenAll(creatureUpdates);
            
            _lastBroadcastTime += (float) deltaTime.TotalSeconds;
            if (_lastBroadcastTime >= BroadcastInterval)
            {
                _lastBroadcastTime = 0;
                await BroadcastPlayersAsync(connectionsSnapshot).ConfigureAwait(false);
                await BroadcastCreaturesAsync(connectionsSnapshot, creaturesSnapshot).ConfigureAwait(false);
            }
        /*}
        finally
        {
            _lock.ExitReadLock();
        }
        */
    }
    
    public IReadOnlyList<IWorldConnection> GetConnections()
    {
        _lock.EnterReadLock();
        try
        {
            return _connections.ToList(); // Return a copy to avoid modifying the original list
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public IReadOnlyList<Creature> GetCreatures()
    {
        _lock.EnterReadLock();
        try
        {
            return _creatures.ToList(); // Return a copy to avoid modifying the original list
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void AddPlayer(IWorldConnection connection)
    {
        _lock.EnterWriteLock();
        try
        {
            _connections.Add(connection);
            Enabled = true;
            foreach (var neighbor in Neighbors)
            {
                neighbor.Enabled = true;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
    
    public void RemovePlayer(IWorldConnection connection)
    {
        _lock.EnterWriteLock();
        try
        {
            _connections.Remove(connection);
            if (_connections.Count == 0)
            {
                _logger.LogInformation("Chunk {ChunkId} is now disabled", Id);
                Enabled = false;
                foreach (var neighbor in Neighbors)
                {
                    if (neighbor.GetConnections().Count == 0)
                    {
                        _logger.LogInformation("Neighbor chunk {ChunkId} is now disabled", neighbor.Id);
                        neighbor.Enabled = false;
                    }
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void SpawnStartingEntities(IPoolManager poolManager)
    {
        _poolManager = poolManager;
        _poolManager.SpawnStartingEntities(this);
    }

    public void AddCreature(Creature creature)
    {
        _lock.EnterWriteLock();
        try
        {
            _creatures.Add(creature);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
    
    public void RemoveCreature(Creature creature)
    {
        _lock.EnterWriteLock();
        try
        {
            _creatures.Remove(creature);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
    
    private async Task BroadcastPlayersAsync(IReadOnlyList<IWorldConnection> connectionsSnapshot)
    {
        var tasks = new List<Task>();

        Parallel.ForEach(connectionsSnapshot, connection =>
        {
            if (!connection.InMap) return;

            var playerPackets = new List<SPlayerPacket>();

            foreach (var otherSession in connectionsSnapshot)
            {
                if (!otherSession.InMap) continue;
                if (otherSession.AccountId == connection.AccountId) continue;

                playerPackets.Add(new SPlayerPacket
                {
                    AccountId = otherSession.AccountId!,
                    CharacterId = otherSession.Character!.Id!.Value,
                    PositionX = otherSession.Character.Movement.Position.x,
                    PositionY = otherSession.Character.Movement.Position.y,
                    PositionZ = otherSession.Character.Movement.Position.z,
                    VelocityX = otherSession.Character.Movement.Velocity.x,
                    VelocityY = otherSession.Character.Movement.Velocity.y,
                    VelocityZ = otherSession.Character.Movement.Velocity.z,
                    Chatting = otherSession.Character!.IsChatting,
                    Elapsed = otherSession.Character.ElapsedGameTime
                });
            }

            if (playerPackets.Count > 0)
            {
                tasks.Add(SendPlayerUpdateAsync(connection, playerPackets));
            }
        });

        await Task.WhenAll(tasks);
    }
    
    private Task SendPlayerUpdateAsync(IWorldConnection connection, List<SPlayerPacket> playerPackets)
    {
        connection.Send(SPlayerPositionUpdatePacket.Create(playerPackets.ToArray(), connection.CryptoSession.Encrypt));
        return Task.CompletedTask;
    }

    private async Task BroadcastCreaturesAsync(IReadOnlyList<IWorldConnection> connectionsSnapshot, IReadOnlyList<Creature> creaturesSnapshot)
    {
        // Broadcast Creature positions
        var creaturePackets = new List<CreaturePacket>();

        foreach (var creature in creaturesSnapshot)
        {
            creaturePackets.Add(new CreaturePacket
            {
                Id = creature.Id,
                Name = creature.Name,
                PositionX = creature.Position.x,
                PositionY = creature.Position.y,
                PositionZ = creature.Position.z,
                VelocityX = creature.Velocity.x,
                VelocityY = creature.Velocity.y,
                VelocityZ = creature.Velocity.z
            });
        }
        
        Parallel.ForEach(connectionsSnapshot, connection =>
        {
            connection.Send(SNpcUpdatePacket.Create(creaturePackets.ToArray(), connection.CryptoSession.Encrypt));
        });
    }

}
