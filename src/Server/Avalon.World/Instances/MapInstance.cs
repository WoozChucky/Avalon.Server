using System.Buffers;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.Filters;
using Avalon.World.Pools;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Spells;
using Avalon.World.Public.Units;
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
        LastEmptyAt = null;
    }

    public void RemoveCharacter(IWorldConnection connection)
    {
        connection.Character!.OnDisconnected();
        _characters.Remove(connection.Character.Guid);
        _connections.Remove(connection.Character.Guid);

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
            MapSessionFilter filter = new(connection);
            connection.Update(deltaTime, filter);
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

        // Step 5: Update entity visibility state per character
        foreach (ICharacter character in _characters.Values)
        {
            character.CharacterGameState.Update(_creatures, _characters, objectSpells, new Dictionary<ObjectGuid, GameEntityFields>());
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
        byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
        using WorldObjectWriter writer = new(buffer);

        List<ObjectAdd> addedObjects = [];
        List<ObjectUpdate> updatedObjects = [];

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

        foreach ((ObjectGuid Guid, GameEntityFields Fields) updatedObject in character.CharacterGameState
                     .UpdatedObjects)
        {
            switch (updatedObject.Guid.Type)
            {
                case ObjectType.Character:
                    if (!_characters.TryGetValue(updatedObject.Guid, out ICharacter? updatedCharacter))
                    {
                        continue;
                    }

                    writer.Write(updatedCharacter, GameEntityFields.CharacterUpdate);
                    break;
                case ObjectType.Creature:
                    if (!_creatures.TryGetValue(updatedObject.Guid, out ICreature? updatedCreature))
                    {
                        continue;
                    }

                    writer.Write(updatedCreature, GameEntityFields.CreatureUpdate);
                    break;
                case ObjectType.SpellProjectile:
                    IWorldObject? spell = _spellSystem.GetSpell(updatedObject.Guid);
                    if (spell is null)
                    {
                        continue;
                    }

                    writer.Write(spell, updatedObject.Fields);
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
            connection.Send(SInstanceStateAddPacket.Create(addedObjects,
                connection.CryptoSession.Encrypt));
        }

        if (_lastBroadcastTime >= BroadcastInterval && updatedObjects.Count > 0)
        {
            connection.Send(SInstanceStateUpdatePacket.Create(updatedObjects,
                connection.CryptoSession.Encrypt));
        }

        if (character.CharacterGameState.RemovedObjects.Count > 0)
        {
            _logger.LogInformation("Found {Count} removed objects",
                character.CharacterGameState.RemovedObjects.Count);
            connection.Send(SInstanceStateRemovePacket.Create(character.CharacterGameState.RemovedObjects.ToHashSet(),
                connection.CryptoSession.Encrypt));
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
}
