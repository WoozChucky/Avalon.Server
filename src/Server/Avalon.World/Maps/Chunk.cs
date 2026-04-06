using System.Buffers;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
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
using Avalon.World.Public.Units;
using Avalon.World.Scripts;
using Avalon.World.Serialization;
using Avalon.World.Spells;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maps;

public class Chunk : IChunk
{
    private const float BroadcastInterval = 0.1f;

    private readonly Dictionary<ObjectGuid, ICharacter> _characters = [];
    private readonly ICreatureRespawner _creatureRespawner;
    private readonly Dictionary<ObjectGuid, ICreature> _creatures = [];
    private readonly ILogger<Chunk> _logger;
    private readonly IPoolManager _poolManager;
    private readonly ISpellQueueSystem _spellSystem;
    private readonly IWorld _world;
    private float _lastBroadcastTime;

    public Chunk(ILoggerFactory loggerFactory, IServiceProvider serviceProvider, ushort mapId, Vector2 position,
        IPoolManager poolManager, IWorld world)
    {
        MapId = mapId;
        Position = position;
        Navigator = new ChunkNavigator(loggerFactory);
        _logger = loggerFactory.CreateLogger<Chunk>();
        _poolManager = poolManager;
        _world = world;
        _spellSystem = new ChunkSpellSystem(loggerFactory, serviceProvider,
            serviceProvider.GetRequiredService<IScriptManager>());
        _creatureRespawner = new CreatureRespawner(this);
        Creature.OnCreatureKilled += OnCreatureKilled;
        Creature.OnUnitAttackAnimation += BroadcastUnitAttackAnimation;
        Creature.OnUnitFinishedCastAnimation += BroadcastFinishCastAnimation;
        Creature.OnUnitInterruptedCastAnimation += BroadcastInterruptedCastAnimation;
        CharacterEntity.OnUnitAttackAnimation += BroadcastUnitAttackAnimation;
        CharacterEntity.OnUnitFinishedCastAnimation += BroadcastFinishCastAnimation;
        CharacterEntity.OnUnitInterruptedCastAnimation += BroadcastInterruptedCastAnimation;
        CharacterEntity.OnUnitDamaged += OnCharacterHit;
    }

    public ushort MapId { get; }
    public Vector2 Position { get; private set; }
    public ChunkId Id { get; set; }
    public bool Enabled { get; set; }
    public required ChunkMetadata Metadata { get; set; }
    public IChunkNavigator Navigator { get; }
    public List<IChunk> Neighbors { get; set; } = [];

    public IReadOnlyDictionary<ObjectGuid, ICharacter> Characters => _characters;
    public IReadOnlyDictionary<ObjectGuid, ICreature> Creatures => _creatures;

    public void BroadcastChunkStateTo(ICharacter character)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
        using WorldObjectWriter writer = new(buffer); // Wrapper around BinaryWriter

        List<ObjectAdd> addedObjects = new();
        List<ObjectUpdate> updatedObjects = new();

        foreach (ObjectGuid addedObjectGuid in character.CharacterGameState.NewObjects)
        {
            switch (addedObjectGuid.Type)
            {
                case ObjectType.Character:
                    if (!_characters.TryGetValue(addedObjectGuid, out ICharacter? addedCharacter))
                    {
                        continue;
                    }

                    writer.Write(addedCharacter, GameEntityFields.All);
                    break;
                case ObjectType.Creature:
                    if (!_creatures.TryGetValue(addedObjectGuid, out ICreature? addedCreature))
                    {
                        continue;
                    }

                    writer.Write(addedCreature, GameEntityFields.All);
                    break;
                case ObjectType.SpellProjectile:
                    IWorldObject? spell = _spellSystem.GetSpell(addedObjectGuid);
                    if (spell is null)
                    {
                        continue;
                    }

                    writer.Write(spell);
                    break;
                default:
                    _logger.LogWarning("Unknown object type {ObjectType} on NewObjects serialization",
                        addedObjectGuid.Type);
                    continue;
            }

            long bytesWritten = writer.BaseStream.Position;

            ObjectAdd obj = new() {Guid = addedObjectGuid.RawValue, Fields = new byte[bytesWritten]};

            buffer.AsSpan(0, (int)bytesWritten).CopyTo(obj.Fields);

            addedObjects.Add(obj);

            writer.Reset();
        }

        foreach ((ObjectGuid Guid, GameEntityFields Fields) updatedObject in
                 character.CharacterGameState.UpdatedObjects)
        {
            switch (updatedObject.Guid.Type)
            {
                case ObjectType.Character:
                    //if (character.Guid == updatedObject.Guid) continue;
                    if (!_characters.TryGetValue(updatedObject.Guid, out ICharacter? updatedCharacter))
                    {
                        continue;
                    }

                    writer.Write(updatedCharacter,
                        GameEntityFields.CharacterUpdate); // can and should pass updatedObject.Fields
                    break;
                case ObjectType.Creature:
                    if (!_creatures.TryGetValue(updatedObject.Guid, out ICreature? updatedCreature))
                    {
                        continue;
                    }

                    writer.Write(updatedCreature,
                        GameEntityFields
                            .CreatureUpdate); // an and should pass updatedObject.Fields or GameEntityFields.CreatureUpdate
                    break;
                case ObjectType.SpellProjectile:
                    IWorldObject? spell = _spellSystem.GetSpell(updatedObject.Guid);
                    if (spell is null)
                    {
                        continue;
                    }

                    writer.Write(spell, updatedObject.Fields); // an and should pass updatedObject.Fields
                    break;
                default:
                    _logger.LogWarning("Unknown object type {ObjectType} on UpdatedObjects serialization",
                        updatedObject.Guid.Type);
                    continue;
            }

            long bytesWritten = writer.BaseStream.Position;

            ObjectUpdate obj = new() {Guid = updatedObject.Guid.RawValue, Fields = new byte[bytesWritten]};

            buffer.AsSpan(0, (int)bytesWritten).CopyTo(obj.Fields);

            updatedObjects.Add(obj);

            writer.Reset();
        }

        ArrayPool<byte>.Shared.Return(buffer);

        if (addedObjects.Count > 0)
        {
            character.Connection.Send(SChunkStateAddPacket.Create(addedObjects,
                character.Connection.CryptoSession.Encrypt));
        }

        if (_lastBroadcastTime >= BroadcastInterval) // _lastBroadcastTime >= BroadcastInterval
        {
            if (updatedObjects.Count > 0)
            {
                character.Connection.Send(SChunkStateUpdatePacket.Create(updatedObjects,
                    character.Connection.CryptoSession.Encrypt));
            }
        }

        if (character.CharacterGameState.RemovedObjects.Count > 0)
        {
            _logger.LogInformation("Found {Count} removed objects", character.CharacterGameState.RemovedObjects.Count);
            character.Connection.Send(SChunkStateRemovePacket.Create(character.CharacterGameState.RemovedObjects,
                character.Connection.CryptoSession.Encrypt));
        }
    }

    public void AddCharacter(IWorldConnection connection)
    {
        connection.Character!.ChunkId = Id;

        _characters[connection.Character.Guid] = connection.Character;

        Enabled = true;
        foreach (IChunk neighbor in Neighbors)
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
            foreach (IChunk neighbor in Neighbors)
            {
                if (neighbor.Characters.Count == 0)
                {
                    _logger.LogInformation("Neighbor chunk {ChunkId} is now disabled", neighbor.Id);
                    neighbor.Enabled = false;
                }
            }
        }
    }

    public void BroadcastUnitHit(IUnit attacker, IUnit target, uint currentHealth, uint damage) =>
        Parallel.ForEach(_characters.Values,
            character =>
            {
                character.Connection.Send(SUnitDamagePacket.Create(attacker.Guid, target.Guid.RawValue, currentHealth,
                    damage, character.Connection.CryptoSession.Encrypt));
            });

    public void RespawnCreature(ICreature creature) => _poolManager.SpawnEntity(this, creature);

    public void RemoveCreature(ICreature creature) => RemoveCreature(creature.Guid);

    public bool QueueSpell(ICharacter character, IUnit? target, ISpell spell) =>
        _spellSystem.QueueSpell(character, target, spell);

    private void OnCreatureKilled(ICreature creature, IUnit killer)
    {
        if (!Enabled || !_creatures.ContainsKey(creature.Guid))
        {
            return;
        }

        // Step 1: Stop the creature scripts.
        // Safe: OnCreatureKilled is invoked from within the single-threaded creature foreach loop
        // in Update(), so this assignment and the loop's Script?.Update() are on the same thread.
        creature.Script = null;

        // Step 2: Schedule the creature to be respawned
        _creatureRespawner.ScheduleRespawn(creature);

        // Step 4: Schedule the loot to be spawned

        // Step 5: Update the killer's experience
        if (killer is ICharacter character)
        {
            CharacterLevelExperience? expRequirement =
                _world.Data.CharacterLevelExperiences.FirstOrDefault(exp => exp.Level == character.Level);
            if (expRequirement is null)
            {
                _logger.LogWarning("Experience requirement for level {Level} not found", character.Level);
                return;
            }

            uint creatureExperience = creature.Metadata.Experience;
            if (character.Experience + creatureExperience >= expRequirement.Experience)
            {
                ulong diff = character.Experience + creatureExperience - expRequirement.Experience;
                character.Level++;
                character.Experience = diff;
                character.RequiredExperience = _world.Data.CharacterLevelExperiences
                    .FirstOrDefault(exp => exp.Level == character.Level)?.Experience ?? 0;
            }
            else
            {
                character.Experience += creatureExperience;
            }
        }
    }

    public async Task InitializeAsync() => await Navigator.LoadAsync(Metadata.MeshFile);

    public void Update(TimeSpan deltaTime)
    {
        if (!Enabled)
        {
            return;
        }

        _lastBroadcastTime += (float)deltaTime.TotalSeconds;

        // Step 1: Update creature respawns
        _creatureRespawner.Update(deltaTime);

        // Step 2: Process character packets
        foreach (ICharacter character in _characters.Values)
        {
            if (character.Map != MapId)
            {
                continue;
            }

            MapSessionFilter filter = new(character.Connection);
            character.Connection.Update(deltaTime, filter);

            character.Update(deltaTime);
        }

        List<IWorldObject> objectSpells = new();

        // Step 3: Spell system update
        _spellSystem.Update(deltaTime, objectSpells);

        // Step 4: Update characters at tick rate
        foreach (ICreature creature in _creatures.Values)
        {
            creature.Script?.Update(deltaTime);
        }
        /*
        Parallel.ForEach(_creatures.Values, creature =>
        {
            creature.Script?.Update(deltaTime);
        });
        */

        foreach (ICharacter character in _characters.Values)
        {
            // Update visibility of game entities
            character.CharacterGameState.Update(_creatures, _characters, objectSpells);
        }

        // Parallel.ForEach(_characters.Values, BroadcastChunkStateTo);
        foreach (ICharacter character in _characters.Values)
        {
            BroadcastChunkStateTo(character);
        }

        if (_lastBroadcastTime >= BroadcastInterval)
        {
            _lastBroadcastTime = 0;
        }
    }

    private void BroadcastUnitAttackAnimation(IUnit attacker, ISpell? spell)
    {
        // Check if unit is part of this chunk's characters or creatures
        bool valid = Creatures.TryGetValue(attacker.Guid, out _) || Characters.TryGetValue(attacker.Guid, out _);
        if (!valid)
        {
            return;
        }

        Parallel.ForEach(_characters.Values, character =>
        {
            //TODO: Extract animation id from spell
            character.Connection.Send(SUnitAttackAnimationPacket.Create(attacker.Guid, 1,
                character.Connection.CryptoSession.Encrypt));
        });
    }

    private void BroadcastFinishCastAnimation(IUnit attacker, ISpell spell)
    {
        // Check if unit is part of this chunk's characters or creatures
        bool valid = Creatures.TryGetValue(attacker.Guid, out _) || Characters.TryGetValue(attacker.Guid, out _);
        if (!valid)
        {
            return;
        }

        Parallel.ForEach(_characters.Values, character =>
        {
            //TODO: Extract animation id from spell
            character.Connection.Send(SUnitFinishCastPacket.Create(attacker.Guid, spell.SpellId,
                character.Connection.CryptoSession.Encrypt));
        });
    }

    private void BroadcastInterruptedCastAnimation(IUnit attacker, ISpell spell)
    {
        // Check if unit is part of this chunk's characters or creatures
        bool valid = Creatures.TryGetValue(attacker.Guid, out _) || Characters.TryGetValue(attacker.Guid, out _);
        if (!valid)
        {
            return;
        }

        Parallel.ForEach(_characters.Values,
            character =>
            {
                character.Connection.Send(SCharacterInterruptedCastPacket.Create(attacker.Guid, spell.SpellId,
                    character.Connection.CryptoSession.Encrypt));
            });
    }

    private void OnCharacterHit(IUnit unit, IUnit attacker, uint damage) =>
        BroadcastUnitHit(attacker, unit, unit.CurrentHealth, damage);

    public void SpawnStartingEntities() => _poolManager.SpawnStartingEntities(this);

    public void AddCreature(ICreature creature) => _creatures.Add(creature.Guid, creature);

    public void RemoveCreature(ObjectGuid id) => _creatures.Remove(id);

    public void BroadcastUniStartCast(IUnit caster, float spellCastTime) =>
        Parallel.ForEach(_characters.Values,
            character =>
            {
                character.Connection.Send(SUnitStartCastPacket.Create(caster.Guid, spellCastTime,
                    character.Connection.CryptoSession.Encrypt));
            });

    public void OnPlayerMoved(IWorldConnection connection)
    {
        Vector3 currentPosition = connection.Character!.Position;
        float updateThresholdDistance = 10f;

        // Check if player is near the border of the current chunk
        bool nearBorder = IsNearChunkBorder(currentPosition, Metadata, updateThresholdDistance);

        // If the player is near the border, send the state of the neighboring chunks
        if (nearBorder)
        {
            List<IChunk> nearbyChunks = GetNearbyChunks(currentPosition, Metadata, updateThresholdDistance);
            foreach (IChunk neighbor in nearbyChunks)
            {
                _logger.LogInformation(
                    "Player {CharacterId} is near the border of chunk {ChunkId}, sending state of neighbor {NeighborId}",
                    connection.Character.Name, Id, neighbor.Id);
                neighbor.BroadcastChunkStateTo(connection.Character);
            }
        }

        // Check if the player has moved to a different chunk
        foreach (IChunk neighbor in Neighbors)
        {
            if (IsWithinChunk(currentPosition, neighbor.Metadata))
            {
                if (neighbor.Id != Id)
                {
                    _logger.LogInformation("Player {CharacterId} moved to chunk {ChunkId}", connection.Character.Name,
                        neighbor.Id);
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
        List<IChunk> nearbyChunks = new();

        float minX = chunkMetadata.Position.x;
        float maxX = chunkMetadata.Position.x + chunkMetadata.Size.x;
        float minZ = chunkMetadata.Position.z;
        float maxZ = chunkMetadata.Position.z + chunkMetadata.Size.z;

        bool nearLeftBorder = position.x <= minX + threshold;
        bool nearRightBorder = position.x >= maxX - threshold;
        bool nearBottomBorder = position.z <= minZ + threshold;
        bool nearTopBorder = position.z >= maxZ - threshold;

        foreach (IChunk neighbor in Neighbors)
        {
            float neighborMinX = neighbor.Metadata.Position.x;
            float neighborMaxX = neighbor.Metadata.Position.x + neighbor.Metadata.Size.x;
            float neighborMinZ = neighbor.Metadata.Position.z;
            float neighborMaxZ = neighbor.Metadata.Position.z + neighbor.Metadata.Size.z;

            if (
                (nearLeftBorder && Mathf.Approximately(neighborMaxX, minX)) ||
                (nearRightBorder && Mathf.Approximately(neighborMinX, maxX)) ||
                (nearBottomBorder && Mathf.Approximately(neighborMaxZ, minZ)) ||
                (nearTopBorder && Mathf.Approximately(neighborMinZ, maxZ)) ||
                (nearLeftBorder && nearBottomBorder && Mathf.Approximately(neighborMaxX, minX) &&
                 Mathf.Approximately(neighborMaxZ, minZ)) ||
                (nearRightBorder && nearBottomBorder && Mathf.Approximately(neighborMinX, maxX) &&
                 Mathf.Approximately(neighborMaxZ, minZ)) ||
                (nearLeftBorder && nearTopBorder && Mathf.Approximately(neighborMaxX, minX) &&
                 Mathf.Approximately(neighborMinZ, maxZ)) ||
                (nearRightBorder && nearTopBorder && Mathf.Approximately(neighborMinX, maxX) &&
                 Mathf.Approximately(neighborMinZ, maxZ))
            )
            {
                nearbyChunks.Add(neighbor);
            }
        }

        return nearbyChunks;
    }

    private bool IsNearChunkBorder(Vector3 position, ChunkMetadata chunkMetadata, float threshold)
    {
        float minX = chunkMetadata.Position.x;
        float maxX = chunkMetadata.Position.x + chunkMetadata.Size.x;
        float minZ = chunkMetadata.Position.z;
        float maxZ = chunkMetadata.Position.z + chunkMetadata.Size.z;

        return position.x < minX + threshold || position.x > maxX - threshold ||
               position.z < minZ + threshold || position.z > maxZ - threshold;
    }

    private bool IsWithinChunk(Vector3 position, ChunkMetadata chunkMetadata) =>
        chunkMetadata.Position.x <= position.x && position.x < chunkMetadata.Position.x + chunkMetadata.Size.x &&
        chunkMetadata.Position.z <= position.z && position.z < chunkMetadata.Position.z + chunkMetadata.Size.z;
}
