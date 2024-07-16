using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.World;
using Avalon.World.Filters;
using Avalon.World.Maps.Navigation;
using Avalon.World.Pools;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using DotRecast.Detour.Crowd;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maps;

public class ObjectPool<T> where T : new()
{
    private readonly Stack<T> _objects = new Stack<T>();

    public T Get()
    {
        return _objects.Count > 0 ? _objects.Pop() : new T();
    }

    public void Return(T obj)
    {
        _objects.Push(obj);
    }
}

public class Chunk : IChunk
{
    public uint Id { get; set; }
    public ushort MapId { get; private set; }
    public bool Enabled { get; set; }
    public Vector2 Position { get; private set; }
    public required ChunkMetadata Metadata { get; init; }
    public IChunkNavigator Navigator { get; private set; }
    public List<IChunk> Neighbors { get; set; } = [];
    
    private readonly List<ICreature> _creatures = [];
    private readonly List<IWorldConnection> _connections = [];
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly ILogger<Chunk> _logger;
    private readonly ObjectPool<List<CreatureStateNew>> _listNewPool = new ObjectPool<List<CreatureStateNew>>();
    private readonly ObjectPool<List<CreatureStateUpdate>> _listUpdatePool = new ObjectPool<List<CreatureStateUpdate>>();
    
    private const float BroadcastInterval = 0.1f;
    
    private IPoolManager _poolManager;
    private float _lastBroadcastTime;
    
    private DtCrowd? _crowd;

    public Chunk(ILoggerFactory loggerFactory, ushort mapId, Vector2 position)
    {
        _logger = loggerFactory.CreateLogger<Chunk>();
        MapId = mapId;
        Position = position;
        Navigator = new ChunkNavigator(loggerFactory);
        //_crowd = new DtCrowd(new DtCrowdConfig(0.5f), Navigator.Mesh as DtNavMesh);
    }

    public async Task InitializeAsync()
    {
        await Navigator.LoadAsync(Metadata.MeshFile);
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

    public IReadOnlyList<ICreature> GetCreatures()
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

    public void Update(TimeSpan deltaTime)
    {
        if (!Enabled) return;
        
        // Step 1: Update DynamicMapTree
        
        // Step 2: Process character packets
        var connectionsSnapshot = GetConnections();
        
        foreach (var connection in connectionsSnapshot)
        {
            if (connection.Character == null || connection.Character.Map != MapId) continue;
            
            var filter = new MapSessionFilter(connection);
            connection.Update(deltaTime, filter);
        }
        
        // Step 3: Run CreatureRespawnScheduler
        
        // Step 4: Update characters at tick rate
        
        
        var creaturesSnapshot = GetCreatures();
        
        Parallel.ForEach(_creatures, creature =>
        {
            creature.Script?.Update(deltaTime);
        });

        _lastBroadcastTime += (float) deltaTime.TotalSeconds;
        
        /*
        foreach (var connection in connectionsSnapshot)
        {
            bool hasNewEntities = false;
            var newCreatures = _listNewPool.Get();
            var updatedCreatures = _listUpdatePool.Get();
        
            foreach (var creature in creaturesSnapshot)
            {
                if (!connection.GameState.KnownEntities.Contains(creature.Id))
                {
                    // Mark the creature as known and collect it as a new entity
                    connection.GameState.KnownEntities.Add(creature.Id);
                    newCreatures.Add(new CreatureStateNew
                    {
                        Id = creature.Id,
                        Name = creature.Name,
                        Position = creature.Position,
                        Velocity = creature.Velocity,
                        Rotation = creature.Orientation.y,
                        Health = creature.Health,
                        Level = 1
                    });
                    hasNewEntities = true;
                }
                else
                {
                    // Collect creature updates
                    updatedCreatures.Add(new CreatureStateUpdate
                    {
                        Id = creature.Id,
                        Position = creature.Position,
                        Velocity = creature.Velocity,
                        Rotation = creature.Orientation.y,
                        Health = creature.Health
                    });
                }
            }
        
            // Send new entities
            if (hasNewEntities)
            {
                var newEntitiesPacket = SWorldGameStatePacket.Create(newCreatures, connection.CryptoSession.Encrypt);
                connection.Send(newEntitiesPacket);
            }

            // Send updates for known entities
            if (updatedCreatures.Any())
            {
                if (_lastBroadcastTime >= BroadcastInterval)
                {
                    var updateEntitiesPacket = SWorldGameStatePacket.Create(updatedCreatures, connection.CryptoSession.Encrypt);
                    connection.Send(updateEntitiesPacket);
                }
            }
            
            _listNewPool.Return(newCreatures);
            _listUpdatePool.Return(updatedCreatures);
        }
        */
        
        if (_lastBroadcastTime >= BroadcastInterval)
        {
            _lastBroadcastTime = 0;
            BroadcastPlayers(connectionsSnapshot);
            BroadcastCreatures(connectionsSnapshot, creaturesSnapshot);
        }
    }

    public void AddPlayer(IWorldConnection connection)
    {
        _lock.EnterWriteLock();
        try
        {
            connection.Character!.ChunkId = Id;
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
            connection.Character!.ChunkId = 0;
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

    public void SendState(IWorldConnection connection)
    {
        var playerPackets = new List<SPlayerPacket>();
        var creaturePackets = new List<CreaturePacket>();

        var connections = GetConnections();
        var creatures = GetCreatures();
        
        foreach (var otherConnection in connections)
        {
            if (otherConnection.AccountId == connection.AccountId) continue;

            playerPackets.Add(new SPlayerPacket
            {
                AccountId = otherConnection.AccountId!,
                CharacterId = otherConnection.Character!.Id,
                PositionX = otherConnection.Character.Position.x,
                PositionY = otherConnection.Character.Position.y,
                PositionZ = otherConnection.Character.Position.z,
                VelocityX = otherConnection.Character.Velocity.x,
                VelocityY = otherConnection.Character.Velocity.y,
                VelocityZ = otherConnection.Character.Velocity.z,
                Chatting = false,
                Elapsed = 0 // TODO: Calculate elapsed time
            });
        }
        
        foreach (var creature in creatures)
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
                VelocityZ = creature.Velocity.z,
                Orientation = creature.Orientation.y,
            });
        }
        
        if (playerPackets.Count > 0)
        {
            connection.Send(SPlayerPositionUpdatePacket.Create(playerPackets.ToArray(), connection.CryptoSession.Encrypt));
        }
        if (creaturePackets.Count > 0)
        {
            connection.Send(SNpcUpdatePacket.Create(creaturePackets.ToArray(), connection.CryptoSession.Encrypt));
        }
    }

    public void SpawnStartingEntities(IPoolManager poolManager)
    {
        _poolManager = poolManager;
        _poolManager.SpawnStartingEntities(this);
    }

    public void AddCreature(ICreature creature)
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
    
    public void RemoveCreature(ICreature creature)
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

    public void RemoveCreature(Guid id)
    {
        _lock.EnterWriteLock();
        try
        {
            var creature = _creatures.FirstOrDefault(c => c.Id == id);
            if (creature != null)
            {
                _creatures.Remove(creature);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
    
    private void BroadcastPlayers(IReadOnlyList<IWorldConnection> connectionsSnapshot)
    {
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
                    CharacterId = otherSession.Character!.Id,
                    PositionX = otherSession.Character.Position.x,
                    PositionY = otherSession.Character.Position.y,
                    PositionZ = otherSession.Character.Position.z,
                    VelocityX = otherSession.Character.Velocity.x,
                    VelocityY = otherSession.Character.Velocity.y,
                    VelocityZ = otherSession.Character.Velocity.z,
                    Chatting = false,
                    Elapsed = 0 // TODO: Calculate elapsed time
                });
            }

            if (playerPackets.Count > 0)
            {
                connection.Send(SPlayerPositionUpdatePacket.Create(playerPackets.ToArray(), connection.CryptoSession.Encrypt));
            }
        });
    }

    private void BroadcastCreatures(IReadOnlyList<IWorldConnection> connectionsSnapshot, IReadOnlyList<ICreature> creaturesSnapshot)
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
                VelocityZ = creature.Velocity.z,
                Orientation = creature.Orientation.y,
            });
            
        }
        
        if (creaturePackets.Count == 0) return;
        
        Parallel.ForEach(connectionsSnapshot, connection =>
        {
            connection.Send(SNpcUpdatePacket.Create(creaturePackets.ToArray(), connection.CryptoSession.Encrypt));
        });
    }

    public void OnPlayerMoved(IWorldConnection connection)
    {
        var currentPosition = connection.Character!.Position;
        var updateThresholdDistance = 10f;

        // Check if player is near the border of the current chunk
        var nearBorder = IsNearChunkBorder(currentPosition, Metadata, updateThresholdDistance);
        
        // If the player is near the border, send the state of the neighboring chunks
        if (nearBorder)
        {
            var nearbyChunks = GetNearbyChunks(currentPosition, Metadata, updateThresholdDistance);
            foreach (var neighbor in nearbyChunks)
            {
                _logger.LogInformation("Player {CharacterId} is near the border of chunk {ChunkId}, sending state of neighbor {NeighborId}", connection.Character.Name, Id, neighbor.Id);
                neighbor.SendState(connection);
            }
        }

        // Check if the player has moved to a different chunk
        foreach (var neighbor in Neighbors)
        {
            if (IsWithinChunk(currentPosition, neighbor.Metadata))
            {
                if (neighbor.Id != Id)
                {
                    _logger.LogInformation("Player {CharacterId} moved to chunk {ChunkId}", connection.Character.Name, neighbor.Id);
                    // Handle chunk change logic
                    RemovePlayer(connection);
                    neighbor.AddPlayer(connection);
                    connection.Character.ChunkId = neighbor.Id;
                    break;
                }
            }
        }
    }
    
    private List<IChunk> GetNearbyChunks(Vector3 position, ChunkMetadata chunkMetadata, float threshold)
    {
        var nearbyChunks = new List<IChunk>();

        var minX = chunkMetadata.Position.x;
        var maxX = chunkMetadata.Position.x + chunkMetadata.Size.x;
        var minZ = chunkMetadata.Position.z;
        var maxZ = chunkMetadata.Position.z + chunkMetadata.Size.z;

        var nearLeftBorder = position.x <= minX + threshold;
        var nearRightBorder = position.x >= maxX - threshold;
        var nearBottomBorder = position.z <= minZ + threshold;
        var nearTopBorder = position.z >= maxZ - threshold;

        foreach (var neighbor in Neighbors)
        {
            var neighborMinX = neighbor.Metadata.Position.x;
            var neighborMaxX = neighbor.Metadata.Position.x + neighbor.Metadata.Size.x;
            var neighborMinZ = neighbor.Metadata.Position.z;
            var neighborMaxZ = neighbor.Metadata.Position.z + neighbor.Metadata.Size.z;

            if (
                (nearLeftBorder && Mathf.Approximately(neighborMaxX, minX)) ||
                (nearRightBorder && Mathf.Approximately(neighborMinX, maxX)) ||
                (nearBottomBorder && Mathf.Approximately(neighborMaxZ, minZ)) ||
                (nearTopBorder && Mathf.Approximately(neighborMinZ, maxZ)) ||
                (nearLeftBorder && nearBottomBorder && Mathf.Approximately(neighborMaxX, minX) && Mathf.Approximately(neighborMaxZ, minZ)) ||
                (nearRightBorder && nearBottomBorder && Mathf.Approximately(neighborMinX, maxX) && Mathf.Approximately(neighborMaxZ, minZ)) ||
                (nearLeftBorder && nearTopBorder && Mathf.Approximately(neighborMaxX, minX) && Mathf.Approximately(neighborMinZ, maxZ)) ||
                (nearRightBorder && nearTopBorder && Mathf.Approximately(neighborMinX, maxX) && Mathf.Approximately(neighborMinZ, maxZ))
            )
            {
                nearbyChunks.Add(neighbor);
            }
        }

        return nearbyChunks;
    }
    
    private bool IsNearChunkBorder(Vector3 position, ChunkMetadata chunkMetadata, float threshold)
    {
        var minX = chunkMetadata.Position.x;
        var maxX = chunkMetadata.Position.x + chunkMetadata.Size.x;
        var minZ = chunkMetadata.Position.z;
        var maxZ = chunkMetadata.Position.z + chunkMetadata.Size.z;

        return position.x < minX + threshold || position.x > maxX - threshold ||
               position.z < minZ + threshold || position.z > maxZ - threshold;
    }

    private bool IsWithinChunk(Vector3 position, ChunkMetadata chunkMetadata)
    {
        return chunkMetadata.Position.x <= position.x && position.x < chunkMetadata.Position.x + chunkMetadata.Size.x &&
               chunkMetadata.Position.z <= position.z && position.z < chunkMetadata.Position.z + chunkMetadata.Size.z;
    }
}
