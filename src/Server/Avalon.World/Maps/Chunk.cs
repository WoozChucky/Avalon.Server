using System.Buffers;
using Avalon.Common;
using Avalon.Common.Mathematics;
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
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Spells;
using Avalon.World.Scripts;
using Avalon.World.Serialization;
using Avalon.World.Spells;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maps;

public class SpellInstance
{
    public required IUnit Caster { get; init; }
    public IUnit? Target { get; set; }
    public required ISpell SpellInfo { get; init; }
    public required Vector3 CastStartPosition { get; init; }
}

public class Chunk : IChunk
{
    public uint Id { get; set; }
    public ushort MapId { get; private set; }
    public bool Enabled { get; set; }
    public Vector2 Position { get; private set; }
    public required ChunkMetadata Metadata { get; set; }
    public IChunkNavigator Navigator { get; private set; }
    public List<IChunk> Neighbors { get; set; } = [];
    
    public IReadOnlyDictionary<ObjectGuid, ICharacter> Characters => _characters;
    public IReadOnlyDictionary<ObjectGuid, ICreature> Creatures => _creatures;
    
    
    private readonly Dictionary<ObjectGuid, ICharacter> _characters = [];
    private readonly Dictionary<ObjectGuid, ICreature> _creatures = [];
    private readonly ILogger<Chunk> _logger;
    
    private const float BROADCAST_INTERVAL = 0.1f;
    
    private IPoolManager _poolManager;
    private readonly IWorld _world;
    private float _lastBroadcastTime;

    private readonly ICreatureRespawner _creatureRespawner;
    private readonly ISpellQueueSystem _spellSystem;

    public Chunk(ILoggerFactory loggerFactory, IServiceProvider serviceProvider, ushort mapId, Vector2 position, IPoolManager poolManager, IWorld world)
    {
        _poolManager = poolManager;
        _world = world;
        _logger = loggerFactory.CreateLogger<Chunk>();
        MapId = mapId;
        Position = position;
        Navigator = new ChunkNavigator(loggerFactory);
        _spellSystem = new ChunkSpellSystem(loggerFactory, serviceProvider, serviceProvider.GetRequiredService<IScriptManager>());
        Creature.OnCreatureKilled += OnCreatureKilled;
        Creature.OnUnitAttackAnimation += BroadcastUnitAttackAnimation;
        Creature.OnUnitFinishedCastAnimation += BroadcastFinishCastAnimation;
        Creature.OnUnitInterruptedCastAnimation += BroadcastInterruptedCastAnimation;
        CharacterEntity.OnUnitAttackAnimation += BroadcastUnitAttackAnimation;
        CharacterEntity.OnUnitFinishedCastAnimation += BroadcastFinishCastAnimation;
        CharacterEntity.OnUnitInterruptedCastAnimation += BroadcastInterruptedCastAnimation;
        CharacterEntity.OnUnitDamaged += OnCharacterHit;
        _creatureRespawner = new CreatureRespawner(this);
    }

    public bool QueueSpell(ICharacter character, IUnit? target, ISpell spell)
    {
        return _spellSystem.QueueSpell(character, target, spell);
    }

    private void OnCreatureKilled(ICreature creature, IUnit killer)
    {
        if (!Enabled || !_creatures.ContainsKey(creature.Guid)) return;

        // Step 1: Stop the creature scripts
        creature.Script = null; // TODO: Double check if this has to be scheduled to run on the main thread
        
        // Step 2: Schedule the creature to be respawned
        _creatureRespawner.ScheduleRespawn(creature);

        // Step 4: Schedule the loot to be spawned
        
        // Step 5: Update the killer's experience
        if (killer is ICharacter character)
        {
            var expRequirement = _world.Data.CharacterLevelExperiences.FirstOrDefault(exp => exp.Level == character.Level);
            if (expRequirement is null)
            {
                _logger.LogWarning("Experience requirement for level {Level} not found", character.Level);
                return;
            }
            
            const uint creatureExperience = 20U; // TODO: Extract from creature template
            if (character.Experience + creatureExperience >= expRequirement.Experience)
            {
                var diff = character.Experience + creatureExperience - expRequirement.Experience;
                character.Level++;
                character.Experience = diff;
                character.RequiredExperience = _world.Data.CharacterLevelExperiences.FirstOrDefault(exp => exp.Level == character.Level)?.Experience ?? 0;
            }
            else
            {
                character.Experience += creatureExperience;
            }
        }
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
            
            character.Update(deltaTime);
        }
        
        var objectSpells = new List<IWorldObject>();
        
        // Step 3: Spell system update
        _spellSystem.Update(deltaTime, objectSpells);
        
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
            character.GameState.Update(_creatures, _characters, objectSpells);
        }
        
        // Parallel.ForEach(_characters.Values, BroadcastChunkStateTo);
        foreach (var character in _characters.Values)
        {
            BroadcastChunkStateTo(character);
        }
        
        if (_lastBroadcastTime >= BROADCAST_INTERVAL)
        {
            _lastBroadcastTime = 0;
        }
    }
    
    public void BroadcastChunkStateTo(ICharacter character)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        using var writer = new WorldObjectWriter(buffer); // Wrapper around BinaryWriter

        var addedObjects = new List<ObjectAdd>();
        var updatedObjects = new List<ObjectUpdate>();

        foreach (var addedObjectGuid in character.GameState.NewObjects)
        {
            
            switch (addedObjectGuid.Type)
            {
                case ObjectType.Character:
                    //if (character.Guid == addedObjectGuid) continue;
                    writer.Write(_characters[addedObjectGuid], GameEntityFields.All);
                    break;
                case ObjectType.Creature:
                    writer.Write(_creatures[addedObjectGuid], GameEntityFields.All);
                    break;
                case ObjectType.SpellProjectile:
                    var spell = _spellSystem.GetSpell(addedObjectGuid);
                    if (spell is null) continue;
                    writer.Write(spell);
                    break;
                default:
                    _logger.LogWarning("Unknown object type {ObjectType} on NewObjects serialization", addedObjectGuid.Type);
                    continue;
            }
                    
            var bytesWritten = writer.BaseStream.Position;
            
            var obj = new ObjectAdd
            {
                Guid = addedObjectGuid.RawValue,
                Fields = new byte[bytesWritten]
            };
            
            buffer.AsSpan(0, (int)bytesWritten).CopyTo(obj.Fields);
            
            addedObjects.Add(obj);
            
            writer.Reset();
        }

        foreach (var updatedObject in character.GameState.UpdatedObjects)
        {
            switch (updatedObject.Guid.Type)
            {
                case ObjectType.Character:
                    //if (character.Guid == updatedObject.Guid) continue;
                    writer.Write(_characters[updatedObject.Guid], GameEntityFields.CharacterUpdate); // can and should pass updatedObject.Fields
                    break;
                case ObjectType.Creature:
                    writer.Write(_creatures[updatedObject.Guid], GameEntityFields.CreatureUpdate); // an and should pass updatedObject.Fields or GameEntityFields.CreatureUpdate
                    break;
                case ObjectType.SpellProjectile:
                    var spell = _spellSystem.GetSpell(updatedObject.Guid);
                    if (spell is null) continue;
                    writer.Write(spell, updatedObject.Fields); // an and should pass updatedObject.Fields
                    break;
                default:
                    _logger.LogWarning("Unknown object type {ObjectType} on UpdatedObjects serialization", updatedObject.Guid.Type);
                    continue;
            }
            
            var bytesWritten = writer.BaseStream.Position;
            
            var obj = new ObjectUpdate
            {
                Guid = updatedObject.Guid.RawValue,
                Fields = new byte[bytesWritten]
            };
            
            buffer.AsSpan(0, (int)bytesWritten).CopyTo(obj.Fields);
            
            updatedObjects.Add(obj);
            
            writer.Reset();
        }
        
        ArrayPool<byte>.Shared.Return(buffer);
        
        if (addedObjects.Count > 0)
        {
            character.Connection.Send(SChunkStateAddPacket.Create(addedObjects, character.Connection.CryptoSession.Encrypt));
        }
        
        if (_lastBroadcastTime >= BROADCAST_INTERVAL) // _lastBroadcastTime >= BroadcastInterval
        {
            if (updatedObjects.Count > 0)
            {
                character.Connection.Send(SChunkStateUpdatePacket.Create(updatedObjects, character.Connection.CryptoSession.Encrypt));
            }
        }
        
        if (character.GameState.RemovedObjects.Count > 0)
        {
            _logger.LogInformation("Found {Count} removed objects", character.GameState.RemovedObjects.Count);
            character.Connection.Send(SChunkStateRemovePacket.Create(character.GameState.RemovedObjects, character.Connection.CryptoSession.Encrypt));
        }
    }

    public void AddCharacter(IWorldConnection connection)
    {
        connection.Character!.ChunkId = Id;
        
        _characters[connection.Character.Guid] = connection.Character;
        
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
            
        _characters.Remove(connection.Character.Guid);
        
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

    private void BroadcastUnitAttackAnimation(IUnit attacker, ISpell spell)
    {
        // Check if unit is part of this chunk's characters or creatures
        var valid = Creatures.TryGetValue(attacker.Guid, out _) || Characters.TryGetValue(attacker.Guid, out _);
        if (!valid) return;
        
        Parallel.ForEach(_characters.Values, character =>
        {
            //TODO: Extract animation id from spell
            character.Connection.Send(SUnitAttackAnimationPacket.Create(attacker.Guid, 1, character.Connection.CryptoSession.Encrypt));
        });
    }
    
    private void BroadcastFinishCastAnimation(IUnit attacker, ISpell spell)
    {
        // Check if unit is part of this chunk's characters or creatures
        var valid = Creatures.TryGetValue(attacker.Guid, out _) || Characters.TryGetValue(attacker.Guid, out _);
        if (!valid) return;
        
        Parallel.ForEach(_characters.Values, character =>
        {
            //TODO: Extract animation id from spell
            character.Connection.Send(SUnitFinishCastPacket.Create(attacker.Guid, spell.SpellId, character.Connection.CryptoSession.Encrypt));
        });
    }

    private void BroadcastInterruptedCastAnimation(IUnit attacker, ISpell spell)
    {
        // Check if unit is part of this chunk's characters or creatures
        var valid = Creatures.TryGetValue(attacker.Guid, out _) || Characters.TryGetValue(attacker.Guid, out _);
        if (!valid) return;

        Parallel.ForEach(_characters.Values, character =>
        {
            character.Connection.Send(SCharacterInterruptedCastPacket.Create(attacker.Guid, spell.SpellId, character.Connection.CryptoSession.Encrypt));
        });
    }

    public void BroadcastUnitHit(IUnit attacker, IUnit target, uint currentHealth, uint damage)
    {
        Parallel.ForEach(_characters.Values, character =>
        {
            character.Connection.Send(SUnitDamagePacket.Create(attacker.Guid, target.Guid.RawValue, currentHealth, damage, character.Connection.CryptoSession.Encrypt));
        });
    }
    
    private void OnCharacterHit(IUnit unit, IUnit attacker, uint damage)
    {
        BroadcastUnitHit(attacker, unit, unit.CurrentHealth, damage);
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
        _creatures.Add(creature.Guid, creature);
    }
    
    public void RemoveCreature(ICreature creature)
    {
        RemoveCreature(creature.Guid);
    }

    public void RemoveCreature(ObjectGuid id)
    {
        _creatures.Remove(id);
    }
    
    public void BroadcastUniStartCast(IUnit caster, float spellCastTime)
    {
        Parallel.ForEach(_characters.Values, character =>
        {
            character.Connection.Send(SUnitStartCastPacket.Create(caster.Guid, spellCastTime, character.Connection.CryptoSession.Encrypt));
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
