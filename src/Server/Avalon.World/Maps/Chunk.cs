using Avalon.Network.Packets.Movement;
using Avalon.World.Pools;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maps;

public class Chunk : IChunk
{
    public uint Id { get; set; }
    public bool Enabled { get; set; }
    public required ChunkMetadata Metadata { get; init; }
    public List<IChunk> Neighbors { get; set; } = [];
    
    private readonly List<ICreature> _creatures = [];
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

    private Task BroadcastCreaturesAsync(IReadOnlyList<IWorldConnection> connectionsSnapshot, IReadOnlyList<ICreature> creaturesSnapshot)
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
        
        return Task.CompletedTask;
    }

}
