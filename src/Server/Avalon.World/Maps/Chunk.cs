using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.World.Filters;
using Avalon.World.Maps.Navigation;
using Avalon.World.Pools;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maps;

public class Chunk : IChunk
{
    public uint Id { get; set; }
    public ushort MapId { get; private set; }
    public bool Enabled { get; set; }
    public Vector2 Position { get; private set; }
    public required ChunkMetadata Metadata { get; init; }
    public IChunkNavigator Navigator { get; private set; }
    public List<IChunk> Neighbors { get; set; } = [];
    
    public IReadOnlyDictionary<CharacterId, ICharacter> Characters => _characters;
    public IReadOnlyDictionary<CreatureId, ICreature> Creatures => _creatures;
    
    
    private readonly Dictionary<CharacterId, ICharacter> _characters = [];
    private readonly Dictionary<CreatureId, ICreature> _creatures = [];
    private readonly ILogger<Chunk> _logger;
    
    private const float BroadcastInterval = 0.1f;
    
    private IPoolManager _poolManager;
    private float _lastBroadcastTime;

    public Chunk(ILoggerFactory loggerFactory, ushort mapId, Vector2 position)
    {
        _logger = loggerFactory.CreateLogger<Chunk>();
        MapId = mapId;
        Position = position;
        Navigator = new ChunkNavigator(loggerFactory);
    }

    public async Task InitializeAsync()
    {
        await Navigator.LoadAsync(Metadata.MeshFile);
    }

    public void Update(TimeSpan deltaTime)
    {
        if (!Enabled) return;
        _lastBroadcastTime += (float) deltaTime.TotalSeconds;
        
        // Step 1: Update DynamicMapTree
        
        // Step 2: Process character packets
        foreach (var character in _characters.Values)
        {
            if (character.Map != MapId) continue;
            
            var filter = new MapSessionFilter(character.Connection);
            character.Connection.Update(deltaTime, filter);
        }
        
        // Step 3: Run CreatureRespawnScheduler
        
        // Step 4: Update characters at tick rate
        Parallel.ForEach(_creatures.Values, creature =>
        {
            creature.Script?.Update(deltaTime);
        });

        foreach (var character in _characters.Values)
        {
            // Update visibility of game entities
            var a = _creatures.Values;
            character.GameState.Update(_creatures, _characters);
        }
        
        Parallel.ForEach(_characters.Values, BroadcastChunkStateTo);
        
        if (_lastBroadcastTime >= BroadcastInterval)
        {
            _lastBroadcastTime = 0;
        }
    }
    
    public void BroadcastChunkStateTo(ICharacter character)
    {
        foreach (var newCharacterId in character.GameState.NewCharacters)
        {
        
        }
                
        foreach (var updatedCharacterId in character.GameState.UpdatedCharacters)
        {
        
        }
                
        foreach (var removedCharacterId in character.GameState.RemovedCharacters)
        {
        
        }
        
        var addedCreatures = new List<CreatureAdd>();
        var updatedCreatures = new List<CreatureUpdate>();
        var removedCreatures = new List<ulong>();
        
        foreach (var newCreatureId in character.GameState.NewCreatures)
        {
            var newCreature = _creatures[newCreatureId];
            addedCreatures.Add(new CreatureAdd
            {
                Id = newCreature.Id,
                TemplateId = newCreature.Metadata.Id,
                Name = newCreature.Name,
                Health = newCreature.Health,
                Power = newCreature.Power,
                Level = newCreature.Level,
                PositionX = newCreature.Position.x,
                PositionY = newCreature.Position.y,
                PositionZ = newCreature.Position.z,
                VelocityX = newCreature.Velocity.x,
                VelocityY = newCreature.Velocity.y,
                VelocityZ = newCreature.Velocity.z,
                Orientation = newCreature.Orientation.y,
                MoveState = newCreature.MoveState
            });
        }
        
        foreach (var updatedCreatureId in character.GameState.UpdatedCreatures)
        {
            //TODO: Send only the updated field indices and values
            var updatedCreature = _creatures[updatedCreatureId];
            updatedCreatures.Add(new CreatureUpdate
            {
                Id = updatedCreature.Id,
                CurrentHealth = updatedCreature.CurrentHealth,
                CurrentPower = updatedCreature.CurrentPower,
                PositionX = updatedCreature.Position.x,
                PositionY = updatedCreature.Position.y,
                PositionZ = updatedCreature.Position.z,
                VelocityX = updatedCreature.Velocity.x,
                VelocityY = updatedCreature.Velocity.y,
                VelocityZ = updatedCreature.Velocity.z,
                Orientation = updatedCreature.Orientation.y,
                MoveState = updatedCreature.MoveState,
                Alive = updatedCreature.CurrentHealth > 0
            });
                    
        }
                
        foreach (var removedCreatureId in character.GameState.RemovedCreatures)
        {
            removedCreatures.Add(_creatures[removedCreatureId].Id);
        }
        
        if (addedCreatures.Count > 0)
        {
            character.Connection.Send(SCreatureAddedPacket.Create(addedCreatures, character.Connection.CryptoSession.Encrypt));
        }
        
        if (_lastBroadcastTime >= BroadcastInterval)
        {
            if (updatedCreatures.Count > 0)
            {
                character.Connection.Send(SCreatureUpdatedPacket.Create(updatedCreatures, character.Connection.CryptoSession.Encrypt));
            }
        }
        
        if (removedCreatures.Count > 0)
        {
            character.Connection.Send(SCreatureRemovedPacket.Create(removedCreatures, character.Connection.CryptoSession.Encrypt));
        }
    }

    public void AddCharacter(IWorldConnection connection)
    {
        connection.Character!.ChunkId = Id;
        
        _characters[connection.Character.Id] = connection.Character;
        
        Enabled = true;
        foreach (var neighbor in Neighbors)
        {
            neighbor.Enabled = true;
        }
    }
    
    public void RemoveCharacter(IWorldConnection connection)
    {
        connection.Character!.ChunkId = 0;
            
        _characters.Remove(connection.Character.Id);
        
        if (_characters.Count == 0)
        {
            _logger.LogInformation("Chunk {ChunkId} is now disabled", Id);
            Enabled = false;
            foreach (var neighbor in Neighbors)
            {
                if (neighbor.Characters.Count == 0)
                {
                    _logger.LogInformation("Neighbor chunk {ChunkId} is now disabled", neighbor.Id);
                    neighbor.Enabled = false;
                }
            }
        }
    }

    public void BroadcastAttackAnimation(CreatureId creatureId, ushort animationId)
    {
        Parallel.ForEach(_characters.Values, character =>
        {
            character.Connection.Send(SCreatureAttackAnimationPacket.Create(creatureId, animationId, character.Connection.CryptoSession.Encrypt));
        });
    }
    
    public void BroadcastCreatureHit(CharacterId attackerId, CreatureId creatureId, uint currentHealth, uint damage)
    {
        Parallel.ForEach(_characters.Values, character =>
        {
            character.Connection.Send(SCreatureDamagePacket.Create(attackerId, creatureId, currentHealth, damage, character.Connection.CryptoSession.Encrypt));
        });
    }

    public void SpawnStartingEntities(IPoolManager poolManager)
    {
        _poolManager = poolManager;
        _poolManager.SpawnStartingEntities(this);
    }

    public void AddCreature(ICreature creature)
    {
        _creatures.Add(creature.Id, creature);
    }
    
    public void RemoveCreature(ICreature creature)
    {
        RemoveCreature(creature.Id);
    }

    public void RemoveCreature(ulong id)
    {
        _creatures.Remove(id);
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
                neighbor.BroadcastChunkStateTo(connection.Character);
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
                    RemoveCharacter(connection);
                    neighbor.AddCharacter(connection);
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
