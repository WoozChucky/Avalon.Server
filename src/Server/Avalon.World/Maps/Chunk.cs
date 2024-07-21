using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.Filters;
using Avalon.World.Maps.Navigation;
using Avalon.World.Pools;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Spells;
using Avalon.World.Spells;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maps;

public class ChunkSpellSystem(ILoggerFactory factory)
{
    private readonly ILogger<ChunkSpellSystem> _logger = factory.CreateLogger<ChunkSpellSystem>();
    
    private readonly HashSet<(ICharacter character, ICreature target, ISpell spell)> _spellQueue = [];
    private readonly List<ISpellProjectile> _activeSpells = [];
    private readonly HashSet<ulong> _removeScheduled = [];
    
    public bool QueueSpell(ICharacter character, ICreature target, ISpell spell)
    {
        return _spellQueue.Add((character, target, spell));
    }
    
    public void Update(TimeSpan deltaTime, out List<ISpellProjectile> activeSpells)
    {
        foreach (var (character, target, spell) in _spellQueue)
        {
            spell.CastTimeTimer -= (float) deltaTime.TotalSeconds;
            
            if (!(spell.CastTimeTimer <= 0)) continue;
            
            spell.CastTimeTimer = spell.CastTime;
            _spellQueue.Remove((character, target, spell));
                
            var projectile = new SpellProjectile
            {
                Id = IGameEntity.GenerateId(),
                Spell = spell,
                Caster = character,
                Target = target,
                Position = character.Position + new Vector3(0, 0.5f, 0),
                Speed = 5f,
                Velocity = Vector3.Normalize(target.Position - character.Position)
            };
                
            _activeSpells.Add(projectile);
                
            _logger.LogInformation("Finished spell {SpellId} cast by {CharacterId} on {CreatureId}", spell.SpellId, character.Id, target.Id);
        }

        foreach (var projectile in _activeSpells)
        {
            projectile.Update(deltaTime);
            
            if (Vector3.Distance(projectile.Position, projectile.Target.Position + ISpellProjectile.HeightOffset) < 0.5f)
            {
                _logger.LogInformation("Spell {SpellId} hit {CreatureId}", projectile.Spell.SpellId, projectile.Target.Id);
                projectile.Target.OnHit(projectile.Caster, 0);
                _removeScheduled.Add(projectile.Id);
            }
        }
        
        foreach (var id in _removeScheduled)
        {
            _activeSpells.RemoveAll(p => p.Id == id);
        }
        
        _removeScheduled.Clear();
        
        activeSpells = _activeSpells;
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
    
    public IReadOnlyDictionary<CharacterId, ICharacter> Characters => _characters;
    public IReadOnlyDictionary<CreatureId, ICreature> Creatures => _creatures;
    
    
    private readonly Dictionary<CharacterId, ICharacter> _characters = [];
    private readonly Dictionary<CreatureId, ICreature> _creatures = [];
    private readonly ILogger<Chunk> _logger;
    
    private const float BroadcastInterval = 0.1f;
    
    private IPoolManager _poolManager;
    private float _lastBroadcastTime;

    private readonly ICreatureRespawner _creatureRespawner;
    
    private ChunkSpellSystem _spellSystem;

    public Chunk(ILoggerFactory loggerFactory, ushort mapId, Vector2 position, IPoolManager poolManager)
    {
        _poolManager = poolManager;
        _logger = loggerFactory.CreateLogger<Chunk>();
        MapId = mapId;
        Position = position;
        Navigator = new ChunkNavigator(loggerFactory);
        _spellSystem = new ChunkSpellSystem(loggerFactory);
        Creature.OnCreatureKilled += OnCreatureKilled;
        _creatureRespawner = new CreatureRespawner(this);
    }
    
    public bool QueueSpell(ICharacter character, ICreature target, ISpell spell)
    {
        return _spellSystem.QueueSpell(character, target, spell);
    }

    private void OnCreatureKilled(ICreature creature, IGameEntity killer)
    {
        if (!Enabled || !_creatures.ContainsKey(creature.Id)) return;

        // Step 1: Stop the creature scripts
        creature.Script = null; // TODO: Double check if this has to be schuduled to run on the main thread
        
        // Step 2: Schedule the creature to be respawned
        _creatureRespawner.ScheduleRespawn(creature);

        // Step 4: Schedule the loot to be spawned
        
        Parallel.ForEach(_characters.Values, _ =>
        {
            // character.Connection.Send(SCreatureDeathPacket.Create(creatureId, character.Connection.CryptoSession.Encrypt));
        });
    }

    public async Task InitializeAsync()
    {
        await Navigator.LoadAsync(Metadata.MeshFile);
    }

    public void Update(TimeSpan deltaTime)
    {
        if (!Enabled) return;
        _lastBroadcastTime += (float) deltaTime.TotalSeconds;
        
        // Step 1: Update creature respawns
        _creatureRespawner.Update(deltaTime);
        
        // Step 2: Process character packets
        foreach (var character in _characters.Values)
        {
            if (character.Map != MapId) continue;
            
            var filter = new MapSessionFilter(character.Connection);
            character.Connection.Update(deltaTime, filter);
        }
        
        // Step 3: Spell queue
        _spellSystem.Update(deltaTime, out var projectiles);
        
        // Step 4: Update characters at tick rate
        foreach (var creature in _creatures.Values)
        {
            creature.Script?.Update(deltaTime);
        }
        /*
        Parallel.ForEach(_creatures.Values, creature =>
        {
            creature.Script?.Update(deltaTime);
        });
        */

        foreach (var character in _characters.Values)
        {
            // Update visibility of game entities
            character.GameState.Update(_creatures, _characters, projectiles);
        }
        
        // Parallel.ForEach(_characters.Values, BroadcastChunkStateTo);
        foreach (var character in _characters.Values)
        {
            BroadcastChunkStateTo(character);
        }
        
        if (_lastBroadcastTime >= BroadcastInterval)
        {
            _lastBroadcastTime = 0;
        }
    }
    
    public void BroadcastChunkStateTo(ICharacter character)
    {
        using var memoryStream = new MemoryStream();
        using var writer = new BinaryWriter(memoryStream);
        
        var addedCharacters = new List<CharacterAdd>();
        var updatedCharacters = new List<(CharacterId characterId, byte[] data)>();
        var removedCharacters = new List<ulong>();
        
        foreach (var newCharacterId in character.GameState.NewCharacters)
        {
            if (newCharacterId == character.Id) continue;
            addedCharacters.Add(new CharacterAdd
            {
                Id = newCharacterId,
                Name = _characters[newCharacterId].Name,
                Level = _characters[newCharacterId].Level,
                PositionX = _characters[newCharacterId].Position.x,
                PositionY = _characters[newCharacterId].Position.y,
                PositionZ = _characters[newCharacterId].Position.z,
                VelocityX = _characters[newCharacterId].Velocity.x,
                VelocityY = _characters[newCharacterId].Velocity.y,
                VelocityZ = _characters[newCharacterId].Velocity.z,
                Orientation = _characters[newCharacterId].Orientation.y,
                MoveState = _characters[newCharacterId].MoveState
            });
        }
                
        foreach (var updatedCharacter in character.GameState.UpdatedCharacters)
        {
            if (updatedCharacter.characterId == character.Id) continue;
            var updated = _characters[updatedCharacter.characterId];
            // TODO: Use code below to filter out fields that have not changed (for now, all fields are sent)
            // var fields = updatedCharacter.fields;
            var fields = GameEntityFields.All;
            
            var data = SerializeFields(memoryStream, writer, updated, fields);
            
            updatedCharacters.Add((updated.Id, data));
        }
                
        foreach (var removedCharacterId in character.GameState.RemovedCharacters)
        {
            if (removedCharacterId == character.Id) continue;
            removedCharacters.Add(removedCharacterId);
        }
        
        var addedCreatures = new List<CreatureAdd>();
        //var updatedCreatures = new List<CreatureUpdate>();
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
        
        var updatedCreatures = new List<(CreatureId creatureId, byte[] data)>();
        
        foreach (var updatedCreature in character.GameState.UpdatedCreatures)
        {
            var updated = _creatures[updatedCreature.creatureId];
            // TODO: Use code below to filter out fields that have not changed (for now, all fields are sent)
            // var fields = updatedCreature.fields;
            var fields = GameEntityFields.All;
            
            var data = SerializeFields(memoryStream, writer, updated, fields);
            
            updatedCreatures.Add((updated.Id, data));
        }
                
        foreach (var removedCreatureId in character.GameState.RemovedCreatures)
        {
            removedCreatures.Add(removedCreatureId);
        }
        
        if (addedCharacters.Count > 0)
        {
            character.Connection.Send(SCharacterAddedPacket.Create(addedCharacters, character.Connection.CryptoSession.Encrypt));
        }
        
        if (true) // _lastBroadcastTime >= BroadcastInterval
        {
            if (updatedCharacters.Count > 0)
            {
                character.Connection.Send(SCharacterUpdatedPacket.Create(updatedCharacters, character.Connection.CryptoSession.Encrypt));
            }
        }
        
        if (removedCharacters.Count > 0)
        {
            character.Connection.Send(SCharacterRemovedPacket.Create(removedCharacters, character.Connection.CryptoSession.Encrypt));
        }
        
        if (addedCreatures.Count > 0)
        {
            character.Connection.Send(SCreatureAddedPacket.Create(addedCreatures, character.Connection.CryptoSession.Encrypt));
        }
        
        if (_lastBroadcastTime >= BroadcastInterval) // _lastBroadcastTime >= BroadcastInterval
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
    
    byte[] SerializeFields(MemoryStream stream, BinaryWriter streamWriter, IGameEntity entity, GameEntityFields changedFields)
    {
        stream.SetLength(0);
        stream.Position = 0;
        streamWriter.Seek(0, SeekOrigin.Begin);
            
        // Write the bitmask
        streamWriter.Write((int)changedFields);

        // Write the updated fields
        if (changedFields.HasFlag(GameEntityFields.Position))
        {
            streamWriter.Write(entity.Position.x);
            streamWriter.Write(entity.Position.y);
            streamWriter.Write(entity.Position.z);
        }
        if (changedFields.HasFlag(GameEntityFields.Velocity))
        {
            streamWriter.Write(entity.Velocity.x);
            streamWriter.Write(entity.Velocity.y);
            streamWriter.Write(entity.Velocity.z);
        }
        if (changedFields.HasFlag(GameEntityFields.Orientation))
        {
            streamWriter.Write(entity.Orientation.y);
        }
        
        if (changedFields.HasFlag(GameEntityFields.CurrentHealth))
        {
            streamWriter.Write(entity.CurrentHealth);
        }
        if (changedFields.HasFlag(GameEntityFields.CurrentPower))
        {
            streamWriter.Write(entity.CurrentPower);
        }
        
        if (changedFields.HasFlag(GameEntityFields.MoveState))
        {
            streamWriter.Write((int) entity.MoveState);
        }
        
        return stream.ToArray();
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
        connection.Character!.OnDisconnected();
        
        connection.Character.ChunkId = 0;
            
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

    public void RespawnCreature(ICreature creature)
    {
        _poolManager.SpawnEntity(this, creature);
    }

    public void SpawnStartingEntities()
    {
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

    public void RemoveCreature(CreatureId id)
    {
        _creatures.Remove(id);
        BroadcastCreatureRemoval(id);
    }

    private void BroadcastCreatureRemoval(CreatureId creatureId)
    {
        Parallel.ForEach(_characters.Values, character =>
        {
            // character.Connection.Send(SCreatureRemovedPacket.Create(creatureId, character.Connection.CryptoSession.Encrypt));
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
