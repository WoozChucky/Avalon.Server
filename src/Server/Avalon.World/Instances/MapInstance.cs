using System.Buffers;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.Pools;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Spells;
using Avalon.World.Public.Units;
using Avalon.World.Public.Scripts;
using Avalon.World.Scripts;
using Avalon.World.Serialization;
using Avalon.World.Spells;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Instances;

public class MapInstance : IMapInstance
{
    private const float BroadcastInterval = 0.1f;

    private readonly Dictionary<ObjectGuid, ICharacter> _characters = [];
    private readonly Dictionary<ObjectGuid, IWorldConnection> _connections = [];
    private readonly ICreatureRespawner _creatureRespawner;
    private readonly Dictionary<ObjectGuid, ICreature> _creatures = [];
    private readonly ILogger<MapInstance> _logger;
    private readonly List<(MapRegion Region, IMapNavigator Navigator)> _navigators = [];
    private readonly IPoolManager _poolManager;
    private readonly ISpellQueueSystem _spellSystem;
    private readonly IWorld _world;
    private float _lastBroadcastTime;
    private readonly Dictionary<ObjectGuid, GameEntityFields> _frameDirtyFields = new(256);
    private readonly Dictionary<ObjectGuid, PerPlayerBroadcastState> _broadcastStates = [];

    public MapInstance(
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider,
        IWorld world,
        IPoolManager poolManager,
        MapTemplateId templateId,
        MapType mapType,
        long? ownerAccountId)
    {
        _logger = loggerFactory.CreateLogger<MapInstance>();
        _world = world;
        _poolManager = poolManager;
        InstanceId = Guid.NewGuid();
        TemplateId = templateId;
        MapType = mapType;
        OwnerAccountId = ownerAccountId;
        AllowedAccounts = ownerAccountId.HasValue ? [ownerAccountId.Value] : [];

        _spellSystem = new InstanceSpellSystem(loggerFactory, serviceProvider,
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
        CharacterEntity.OnSelfDamaged += OnCharacterSelfDamaged;
    }

    public Guid InstanceId { get; }
    public MapTemplateId TemplateId { get; }
    public MapType MapType { get; }
    public long? OwnerAccountId { get; }
    public IReadOnlyList<long> AllowedAccounts { get; }
    public int PlayerCount => _characters.Count;
    public DateTime? LastEmptyAt { get; private set; }

    public IReadOnlyDictionary<ObjectGuid, ICharacter> Characters => _characters;
    public IReadOnlyDictionary<ObjectGuid, ICreature> Creatures => _creatures;

    public bool IsExpired(TimeSpan expiry) =>
        LastEmptyAt.HasValue && (DateTime.UtcNow - LastEmptyAt.Value) >= expiry;

    public bool CanAcceptPlayer(ushort maxPlayers) => _characters.Count < maxPlayers;

    /// <summary>Registers a loaded navigator for a map region.</summary>
    public void AddNavigator(MapRegion region, IMapNavigator navigator) =>
        _navigators.Add((region, navigator));

    public IMapNavigator GetNavigatorForPosition(Vector3 position)
    {
        foreach ((MapRegion region, IMapNavigator navigator) in _navigators)
        {
            if (position.x >= region.Position.x &&
                position.x < region.Position.x + region.Size.x &&
                position.z >= region.Position.z &&
                position.z < region.Position.z + region.Size.z)
            {
                return navigator;
            }
        }

        if (_navigators.Count > 0)
        {
            return _navigators[0].Navigator;
        }

        throw new InvalidOperationException($"No navigators loaded for instance {InstanceId}.");
    }

    public void SpawnStartingEntities() =>
        _poolManager.SpawnStartingEntities(this, _navigators.Select(n => n.Region).ToList());

    public void AddCharacter(IWorldConnection connection)
    {
        _characters[connection.Character!.Guid] = connection.Character;
        _connections[connection.Character.Guid] = connection;
        _broadcastStates[connection.Character.Guid] = new PerPlayerBroadcastState();
        LastEmptyAt = null;
    }

    public void RemoveCharacter(IWorldConnection connection)
    {
        connection.Character!.OnDisconnected();
        _characters.Remove(connection.Character.Guid);
        _connections.Remove(connection.Character.Guid);
        _broadcastStates.Remove(connection.Character.Guid);

        if (_characters.Count == 0)
        {
            LastEmptyAt = DateTime.UtcNow;
            _logger.LogInformation("Instance {InstanceId} (map {TemplateId}) is now empty", InstanceId, TemplateId);
        }
    }

    public void AddCreature(ICreature creature) => _creatures[creature.Guid] = creature;

    public void RemoveCreature(ICreature creature) => _creatures.Remove(creature.Guid);

    public bool QueueSpell(ICharacter caster, IUnit? target, ISpell spell) =>
        _spellSystem.QueueSpell(caster, target, spell);

    public void RespawnCreature(ICreature creature) =>
        _poolManager.SpawnEntity(this, _navigators.Select(n => n.Region).ToList(), creature);

    public void BroadcastUnitHit(IUnit attacker, IUnit target, uint currentHealth, uint damage)
    {
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            connection.Send(SUnitDamagePacket.Create(attacker.Guid, target.Guid.RawValue,
                currentHealth, damage, connection.CryptoSession.Encrypt));
        }
    }

    public void BroadcastUnitStartCast(IUnit caster, float castTime)
    {
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            connection.Send(SUnitStartCastPacket.Create(caster.Guid, castTime,
                connection.CryptoSession.Encrypt));
        }
    }

    public void Update(TimeSpan deltaTime)
    {
        if (_characters.Count == 0)
        {
            return;
        }

        _lastBroadcastTime += (float)deltaTime.TotalSeconds;

        // Step 1: Update creature respawns
        _creatureRespawner.Update(deltaTime);

        // Step 2: Process character packets
        foreach ((ObjectGuid guid, ICharacter character) in _characters)
        {
            IWorldConnection connection = _connections[guid];
            connection.UpdateMap();
            character.Update(deltaTime);
        }

        List<IWorldObject> objectSpells = [];

        // Step 3: Spell system update
        _spellSystem.Update(deltaTime, objectSpells);

        // Step 4: Update creature scripts
        foreach (ICreature creature in _creatures.Values)
        {
            creature.Script?.Update(deltaTime);
        }

        // Step 5a: Snapshot dirty fields for this frame (once, before any client broadcast)
        _frameDirtyFields.Clear();

        foreach (var creature in _creatures.Values)
        {
            var dirty = creature.ConsumeDirtyFields();
            if (dirty != GameEntityFields.None)
                _frameDirtyFields[creature.Guid] = dirty;
        }

        foreach (var character in _characters.Values)
        {
            var dirty = character.ConsumeDirtyFields();
            if (dirty != GameEntityFields.None)
                _frameDirtyFields[character.Guid] = dirty;
        }

        foreach (var obj in objectSpells)
        {
            if (obj is SpellScript spell)
            {
                var dirty = spell.ConsumeDirtyFields();
                if (dirty != GameEntityFields.None)
                    _frameDirtyFields[spell.Guid] = dirty;
            }
        }

        // Step 5b: Update entity visibility state per character
        foreach (ICharacter character in _characters.Values)
        {
            character.CharacterGameState.Update(_creatures, _characters, objectSpells, _frameDirtyFields);
        }

        // Step 6: Broadcast instance state to each character
        foreach (ICharacter character in _characters.Values)
        {
            BroadcastStateTo(character);
        }

        if (_lastBroadcastTime >= BroadcastInterval)
        {
            _lastBroadcastTime = 0;
        }
    }

    private void BroadcastStateTo(ICharacter character)
    {
        IWorldConnection connection = _connections[character.Guid];
        PerPlayerBroadcastState state = _broadcastStates[character.Guid];

        // One rented buffer accumulates all entity blobs for this player.
        // 65 536 bytes ≈ 800 entities at ~80 bytes each (GameEntityFields.All);
        // worst-case per-entity (character with max-length name) is ~300 bytes,
        // giving headroom for ~200 entities before the guard below triggers.
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            // Sync per-tick broadcast path: await using would force this method async and
            // ripple through the 60Hz tick loop; the underlying MemoryStream has nothing to flush.
#pragma warning disable MA0045
            using WorldObjectWriter writer = new(buffer);
#pragma warning restore MA0045

            state.AddedObjects.Clear();
            state.UpdatedObjects.Clear();

            foreach (ObjectGuid addedObjectGuid in character.CharacterGameState.NewObjects)
            {
                if (buffer.Length - (int)writer.BaseStream.Position < 2048)
                {
                    // IMPORTANT: This guard is a last-resort safety valve. Triggering it requires
                    // 200+ simultaneous entity adds per player (at ~80 bytes each for typical entities,
                    // or 32+ entities at worst-case ~300 bytes for a character with a maximum-length name).
                    // A skipped add will NOT be retried: EntityTrackingSystem already marked this GUID
                    // as known, so it will not re-appear in NewObjects next tick. The client will receive
                    // subsequent delta updates for an entity it has never seen, producing a corrupt state.
                    // If this warning appears in production, the buffer size (currently 65536) must be increased.
                    _logger.LogWarning(
                        "BroadcastStateTo: buffer capacity exhausted — skipped add for object {Guid} (client state corrupt until reconnect)",
                        addedObjectGuid.RawValue);
                    continue;
                }

                int startOffset = (int)writer.BaseStream.Position;

                switch (addedObjectGuid.Type)
                {
                    case ObjectType.Character:
                        if (!_characters.TryGetValue(addedObjectGuid, out ICharacter? addedCharacter))
                            continue;
                        writer.Write(addedCharacter, GameEntityFields.All);
                        break;
                    case ObjectType.Creature:
                        if (!_creatures.TryGetValue(addedObjectGuid, out ICreature? addedCreature))
                            continue;
                        writer.Write(addedCreature, GameEntityFields.All);
                        break;
                    case ObjectType.SpellProjectile:
                        IWorldObject? addedSpell = _spellSystem.GetSpell(addedObjectGuid);
                        if (addedSpell is null)
                            continue;
                        writer.Write(addedSpell);
                        break;
                    default:
                        _logger.LogWarning("Unknown object type {ObjectType} on NewObjects serialization",
                            addedObjectGuid.Type);
                        continue;
                }

                int length = (int)writer.BaseStream.Position - startOffset;
                // NOTE: Fields slice is valid only until ArrayPool.Shared.Return(buffer) in the finally block.
                // PacketSerializationHelper.Serialize (called inside Create) is synchronous,
                // so the slice is consumed before Return is reached.
                state.AddedObjects.Add(new ObjectAdd
                {
                    Guid   = addedObjectGuid.RawValue,
                    Fields = new ReadOnlyMemory<byte>(buffer, startOffset, length)
                });
                // No writer.Reset() — blobs accumulate contiguously in buffer.
            }

            foreach ((ObjectGuid Guid, GameEntityFields Fields) updatedObject
                     in character.CharacterGameState.UpdatedObjects)
            {
                if (buffer.Length - (int)writer.BaseStream.Position < 2048)
                {
                    // Safety valve — see the matching guard in the NewObjects loop above.
                    // Skipped delta updates are less severe: the entity state is stale for one tick
                    // and will be corrected when the entity's dirty fields are set again.
                    _logger.LogWarning(
                        "BroadcastStateTo: buffer capacity exhausted — skipped update for object {Guid}",
                        updatedObject.Guid.RawValue);
                    continue;
                }

                int startOffset = (int)writer.BaseStream.Position;

                switch (updatedObject.Guid.Type)
                {
                    case ObjectType.Character:
                        if (!_characters.TryGetValue(updatedObject.Guid, out ICharacter? updatedCharacter))
                            continue;
                        writer.Write(updatedCharacter, GameEntityFields.CharacterUpdate);
                        break;
                    case ObjectType.Creature:
                        if (!_creatures.TryGetValue(updatedObject.Guid, out ICreature? updatedCreature))
                            continue;
                        writer.Write(updatedCreature, GameEntityFields.CreatureUpdate);
                        break;
                    case ObjectType.SpellProjectile:
                        IWorldObject? updatedSpell = _spellSystem.GetSpell(updatedObject.Guid);
                        if (updatedSpell is null)
                            continue;
                        writer.Write(updatedSpell, updatedObject.Fields);
                        break;
                    default:
                        _logger.LogWarning("Unknown object type {ObjectType} on UpdatedObjects serialization",
                            updatedObject.Guid.Type);
                        continue;
                }

                int length = (int)writer.BaseStream.Position - startOffset;
                // NOTE: Fields slice is valid only until ArrayPool.Shared.Return(buffer) in the finally block.
                state.UpdatedObjects.Add(new ObjectUpdate
                {
                    Guid   = updatedObject.Guid.RawValue,
                    Fields = new ReadOnlyMemory<byte>(buffer, startOffset, length)
                });
                // No writer.Reset() — blobs accumulate contiguously in buffer.
            }

            // Create() calls PacketSerializationHelper.Serialize, which reads Fields slices
            // synchronously. The buffer is returned in the finally block after this method returns.
            if (state.AddedObjects.Count > 0)
                connection.Send(SInstanceStateAddPacket.Create(state.AddedObjects, connection.CryptoSession.Encrypt));

            if (_lastBroadcastTime >= BroadcastInterval && state.UpdatedObjects.Count > 0)
                connection.Send(SInstanceStateUpdatePacket.Create(state.UpdatedObjects, connection.CryptoSession.Encrypt));

            if (character.CharacterGameState.RemovedObjects.Count > 0)
            {
                _logger.LogInformation("Found {Count} removed objects",
                    character.CharacterGameState.RemovedObjects.Count);
                connection.Send(SInstanceStateRemovePacket.Create(
                    character.CharacterGameState.RemovedObjects, connection.CryptoSession.Encrypt));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void BroadcastUnitAttackAnimation(IUnit attacker, ISpell? spell)
    {
        if (!_creatures.ContainsKey(attacker.Guid) && !_characters.ContainsKey(attacker.Guid))
        {
            return;
        }

        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            connection.Send(SUnitAttackAnimationPacket.Create(attacker.Guid, 1,
                connection.CryptoSession.Encrypt));
        }
    }

    private void BroadcastFinishCastAnimation(IUnit attacker, ISpell spell)
    {
        if (!_creatures.ContainsKey(attacker.Guid) && !_characters.ContainsKey(attacker.Guid))
        {
            return;
        }

        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            connection.Send(SUnitFinishCastPacket.Create(attacker.Guid, spell.SpellId,
                connection.CryptoSession.Encrypt));
        }
    }

    private void BroadcastInterruptedCastAnimation(IUnit attacker, ISpell spell)
    {
        if (!_creatures.ContainsKey(attacker.Guid) && !_characters.ContainsKey(attacker.Guid))
        {
            return;
        }

        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            connection.Send(SCharacterInterruptedCastPacket.Create(attacker.Guid, spell.SpellId,
                connection.CryptoSession.Encrypt));
        }
    }

    private void OnCharacterHit(IUnit unit, IUnit attacker, uint damage) =>
        BroadcastUnitHit(attacker, unit, unit.CurrentHealth, damage);

    private void OnCharacterSelfDamaged(IUnit unit, IUnit attacker, uint damage)
    {
        if (unit is not ICharacter character || !_connections.TryGetValue(character.Guid, out IWorldConnection? connection))
        {
            return;
        }

        connection.Send(SCharacterDamagePacket.Create(attacker.Guid.RawValue, character.Guid.RawValue,
            character.CurrentHealth, damage, null, connection.CryptoSession.Encrypt));
    }

    private void OnCreatureKilled(ICreature creature, IUnit killer)
    {
        if (!_creatures.ContainsKey(creature.Guid))
        {
            return;
        }

        creature.Script = null;
        _creatureRespawner.ScheduleRespawn(creature);

        if (killer is not ICharacter character)
        {
            return;
        }

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

    private sealed class PerPlayerBroadcastState
    {
        // Capacities sized for a typical instance (32 entities visible per player).
        // List<T> grows automatically if exceeded — this avoids early reallocation.
        public List<ObjectAdd>    AddedObjects   { get; } = new(32);
        public List<ObjectUpdate> UpdatedObjects { get; } = new(32);
    }
}
