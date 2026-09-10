using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.ChunkLayouts;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Abilities;
using Avalon.World.Combat;
using Avalon.World.Public.Combat;
using Avalon.World.Scripts;
using Avalon.World.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Instances;

public class MapInstance : IMapInstance, IPortalSink
{
    private const float BroadcastInterval = 0.1f;

    private readonly Dictionary<ObjectGuid, ICharacter> _characters = [];
    private readonly Dictionary<ObjectGuid, IWorldConnection> _connections = [];
    private readonly ICreatureRespawner _creatureRespawner;
    private readonly Dictionary<ObjectGuid, ICreature> _creatures = [];
    private readonly ILogger<MapInstance> _logger;
    private readonly IMapNavigator _navigator;
    private readonly IAbilityCastSystem _abilityCastSystem;
    private readonly EncounterRegistry _encounterRegistry;
    private readonly CombatService _combatService;
    private readonly ThreatBroadcastService _threatBroadcast;
    private readonly IWorld _world;
    private float _lastBroadcastTime;
    private readonly Dictionary<ObjectGuid, GameEntityFields> _frameDirtyFields = new(256);
    private readonly Dictionary<ObjectGuid, PerPlayerBroadcastState> _broadcastStates = [];
    private readonly List<PortalInstance> _portals = new();

    public MapInstance(
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider,
        IWorld world,
        MapTemplateId templateId,
        uint? ownerCharacterId,
        ChunkLayout layout,
        IMapNavigator navigator,
        int seed,
        MapType mapType = MapType.Normal)
    {
        _logger = loggerFactory.CreateLogger<MapInstance>();
        _world = world;
        InstanceId = Guid.NewGuid();
        TemplateId = templateId;
        MapType = mapType;
        OwnerCharacterId = ownerCharacterId;
        AllowedCharacters = ownerCharacterId.HasValue ? new[] { ownerCharacterId.Value } : Array.Empty<uint>();
        Layout = layout;
        EntrySpawnWorldPos = layout.EntrySpawnWorldPos;
        Seed = seed;
        _navigator = navigator;

        _creatureRespawner = new NoOpCreatureRespawner();

        // Per-instance combat state. CombatConfig is a process-wide singleton (V1: defaults);
        // EncounterRegistry + CombatService are instance-scoped so encounters cannot bleed
        // between MapInstances.
        CombatConfig combatConfig = serviceProvider.GetRequiredService<CombatConfig>();
        _encounterRegistry = new EncounterRegistry(combatConfig);
        _combatService     = new CombatService(combatConfig, _encounterRegistry, this);
        _threatBroadcast   = new ThreatBroadcastService(combatConfig);

        // Cast system gets `this` as ISimulationContext so it can forward the context to
        // ability scripts (E7): scripts route damage through CombatService.ApplyDamage rather
        // than directly calling Target.OnHit. CombatService must be assigned BEFORE this so
        // any first-tick cast resolves through a non-null service.
        _abilityCastSystem = new InstanceAbilityCastSystem(loggerFactory, serviceProvider,
            serviceProvider.GetRequiredService<IScriptManager>(), this);

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
    public uint? OwnerCharacterId { get; }
    public IReadOnlyList<uint> AllowedCharacters { get; }
    public int PlayerCount => _characters.Count;
    public DateTime? LastEmptyAt { get; private set; }
    public int Seed { get; }
    public ChunkLayout? Layout { get; }
    public Vector3? EntrySpawnWorldPos { get; }
    public IReadOnlyList<PortalInstance> Portals => _portals;

    public IReadOnlyDictionary<ObjectGuid, ICharacter> Characters => _characters;
    public IReadOnlyDictionary<ObjectGuid, ICreature> Creatures => _creatures;
    public ICombatService CombatService => _combatService;

    public bool IsExpired(TimeSpan expiry) =>
        LastEmptyAt.HasValue && (DateTime.UtcNow - LastEmptyAt.Value) >= expiry;

    public bool CanAcceptPlayer(ushort maxPlayers) => _characters.Count < maxPlayers;

    public void AddPortal(PortalInstance portal) => _portals.Add(portal);

    public IMapNavigator GetNavigatorForPosition(Vector3 position) => _navigator;

    public void AddCharacter(IWorldConnection connection)
    {
        // LastInputSeq is intentionally NOT reset here. Resetting created a race: any stale
        // PlayerInputPacket from the previous instance still in-flight in the inbound queue
        // would land after this reset with a high seq, bumping LastInputSeq up — then the
        // client's freshly-reset _nextSeq=1 packets would all be rejected by
        // PlayerInputHandler's monotonicity guard. Instead, both sides keep _nextSeq /
        // LastInputSeq monotonic for the connection's lifetime; stale inputs from the
        // previous instance arrive with a seq < current LastInputSeq and get correctly
        // rejected, while new post-transition inputs arrive with a higher seq and pass.

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
        _threatBroadcast.Forget(connection);

        if (_characters.Count == 0)
        {
            LastEmptyAt = DateTime.UtcNow;
            _logger.LogInformation("Instance {InstanceId} (map {TemplateId}) is now empty", InstanceId, TemplateId);
        }
    }

    public void AddCreature(ICreature creature) => _creatures[creature.Guid] = creature;

    public void RemoveCreature(ICreature creature) => _creatures.Remove(creature.Guid);

    public bool QueueAbility(ICharacter caster, IUnit? target, IAbility ability) =>
        _abilityCastSystem.QueueAbility(caster, target, ability);

    public void RunInstantAbility(IUnit caster, IUnit? target, IAbility ability) =>
        _abilityCastSystem.RunInstant(caster, target, ability);

    public void RespawnCreature(ICreature creature)
    {
        // No-op: chunk-layout instances install NoOpCreatureRespawner so this
        // path is never invoked. Kept to satisfy ISimulationContext contract.
    }

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

    public void BroadcastUnitDeath(IUnit unit, IUnit? killer)
    {
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            connection.Send(SUnitDeathPacket.Create(unit.Guid, killer?.Guid,
                connection.CryptoSession.Encrypt));
        }
    }

    public void BroadcastUnitRevive(IUnit unit, Vector3 position, uint health)
    {
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            connection.Send(SUnitRevivePacket.Create(unit.Guid, position, health,
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

        List<IWorldObject> objectAbilities = [];

        // Step 3: Ability cast system update
        _abilityCastSystem.Update(deltaTime, objectAbilities);

        // Step 3b: Tick combat service — decays threat, ends stale encounters.
        _combatService.Update(deltaTime);

        // Step 3c: Mirror threat lists for each player's currently-targeted hostile.
        // Throttled (250 ms / 5 % delta) inside the service; iterating _connections.Values
        // here is safe because no inbound packet handler dequeued above mutates _connections
        // (target-unit just stores a ulong on the connection itself).
        _threatBroadcast.Tick(_connections.Values, _creatures, _combatService);

        // Step 4: Update creature scripts
        foreach (ICreature creature in _creatures.Values)
        {
            creature.Script?.Update(deltaTime);
        }

        // Step 5a: Snapshot dirty fields — ONLY on broadcast ticks. Entity _dirtyFields use
        // |= to accumulate, so OR-ing all changes between broadcasts is captured by a single
        // ConsumeDirtyFields() at broadcast time. Consuming every tick (with sends gated to
        // 10Hz) silently drops every state-change that happened on a non-broadcast tick —
        // notably "creature stopped moving" frames after combat/return resolve, which made
        // creatures appear to drift forever on clients.
        _frameDirtyFields.Clear();
        bool shouldBroadcastUpdates = _lastBroadcastTime >= BroadcastInterval;

        if (shouldBroadcastUpdates)
        {
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

            foreach (var obj in objectAbilities)
            {
                if (obj is AbilityScript ability)
                {
                    var dirty = ability.ConsumeDirtyFields();
                    if (dirty != GameEntityFields.None)
                        _frameDirtyFields[ability.Guid] = dirty;
                }
            }
        }

        // Step 5b: Update entity visibility state per character
        foreach (ICharacter character in _characters.Values)
        {
            character.CharacterGameState.Update(_creatures, _characters, objectAbilities, _frameDirtyFields);
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

        state.AddedObjects.Clear();
        state.UpdatedObjects.Clear();

        foreach (ObjectGuid addedObjectGuid in character.CharacterGameState.NewObjects)
        {
            ObjectState? added = DescribeNewObject(addedObjectGuid, character.Guid);

            if (added is not null)
            {
                state.AddedObjects.Add(added);
            }
        }

        foreach ((ObjectGuid Guid, GameEntityFields Fields) updatedObject
                 in character.CharacterGameState.UpdatedObjects)
        {
            ObjectState? updated = DescribeUpdatedObject(updatedObject, character.Guid);

            if (updated is not null)
            {
                state.UpdatedObjects.Add(updated);
            }
        }

        if (state.AddedObjects.Count > 0)
            connection.Send(SInstanceStateAddPacket.Create(state.AddedObjects, connection.CryptoSession.Encrypt));

        // _frameDirtyFields is populated only on broadcast ticks (see Step 5a in Update),
        // so UpdatedObjects.Count > 0 already implies a broadcast cadence hit.
        if (state.UpdatedObjects.Count > 0)
            connection.Send(SInstanceStateUpdatePacket.Create(state.UpdatedObjects, connection.CryptoSession.Encrypt));

        if (character.CharacterGameState.RemovedObjects.Count > 0)
        {
            _logger.LogInformation("Found {Count} removed objects",
                character.CharacterGameState.RemovedObjects.Count);
            connection.Send(SInstanceStateRemovePacket.Create(
                character.CharacterGameState.RemovedObjects, connection.CryptoSession.Encrypt));
        }
    }

    /// <summary>
    /// An entity the recipient has not seen before, described in full. Null when the entity
    /// has left the instance between being noticed and being described.
    /// </summary>
    private ObjectState? DescribeNewObject(ObjectGuid guid, ObjectGuid recipientGuid)
    {
        switch (guid.Type)
        {
            case ObjectType.Character:
                if (!_characters.TryGetValue(guid, out ICharacter? addedCharacter))
                    return null;
                return ObjectStateWriter.From(
                    addedCharacter,
                    MaskSelfSuppression(GameEntityFields.All, guid, recipientGuid));

            case ObjectType.Creature:
                if (!_creatures.TryGetValue(guid, out ICreature? addedCreature))
                    return null;
                return ObjectStateWriter.From(addedCreature, GameEntityFields.All);

            case ObjectType.SpellProjectile:
                IWorldObject? addedAbility = _abilityCastSystem.GetAbility(guid);
                return addedAbility is null ? null : ObjectStateWriter.From(addedAbility);

            case ObjectType.Portal:
                PortalInstance? addedPortal = _portals.Find(p => p.Guid == guid);
                return addedPortal is null ? null : ObjectStateWriter.From(addedPortal);

            default:
                _logger.LogWarning("Unknown object type {ObjectType} on NewObjects serialization", guid.Type);
                return null;
        }
    }

    /// <summary>
    /// A change to an entity the recipient already has. Null when the entity has left the
    /// instance, and for portals, which never change.
    /// </summary>
    private ObjectState? DescribeUpdatedObject(
        (ObjectGuid Guid, GameEntityFields Fields) updatedObject,
        ObjectGuid recipientGuid)
    {
        switch (updatedObject.Guid.Type)
        {
            case ObjectType.Character:
                if (!_characters.TryGetValue(updatedObject.Guid, out ICharacter? updatedCharacter))
                    return null;
                return ObjectStateWriter.From(
                    updatedCharacter,
                    MaskSelfSuppression(GameEntityFields.CharacterUpdate, updatedObject.Guid, recipientGuid));

            case ObjectType.Creature:
                if (!_creatures.TryGetValue(updatedObject.Guid, out ICreature? updatedCreature))
                    return null;
                return ObjectStateWriter.From(updatedCreature, GameEntityFields.CreatureUpdate);

            case ObjectType.SpellProjectile:
                IWorldObject? updatedAbility = _abilityCastSystem.GetAbility(updatedObject.Guid);
                return updatedAbility is null
                    ? null
                    : ObjectStateWriter.From(updatedAbility, updatedObject.Fields);

            case ObjectType.Portal:
                return null; // portals are immutable in PoC — no delta updates

            default:
                _logger.LogWarning("Unknown object type {ObjectType} on UpdatedObjects serialization",
                    updatedObject.Guid.Type);
                return null;
        }
    }

    private void BroadcastUnitAttackAnimation(IUnit attacker, IAbility? spell)
    {
        if (!_creatures.ContainsKey(attacker.Guid) && !_characters.ContainsKey(attacker.Guid))
        {
            return;
        }

        ushort animationId = ResolveBroadcastAnimationId(spell);
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            connection.Send(SUnitAttackAnimationPacket.Create(attacker.Guid, animationId,
                connection.CryptoSession.Encrypt));
        }
    }

    /// <summary>
    /// Pure helper, exposed for unit tests. Returns the AnimationId the broadcast
    /// should carry: the ability's metadata AnimationId, or 1 (legacy default for melee
    /// auto-attacks) when no ability backs the swing.
    /// AbilityMetadata stores AnimationId as uint; the wire packet field is ushort
    /// (animation IDs are practically &lt;= 65535).
    /// </summary>
    public static ushort ResolveBroadcastAnimationId(IAbility? ability)
        => (ushort)(ability?.Metadata.AnimationId ?? 1u);

    private void BroadcastFinishCastAnimation(IUnit attacker, IAbility spell)
    {
        if (!_creatures.ContainsKey(attacker.Guid) && !_characters.ContainsKey(attacker.Guid))
        {
            return;
        }

        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            connection.Send(SUnitFinishCastPacket.Create(attacker.Guid, spell.AbilityId,
                connection.CryptoSession.Encrypt));
        }
    }

    private void BroadcastInterruptedCastAnimation(IUnit attacker, IAbility spell)
    {
        if (!_creatures.ContainsKey(attacker.Guid) && !_characters.ContainsKey(attacker.Guid))
        {
            return;
        }

        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            connection.Send(SCharacterInterruptedCastPacket.Create(attacker.Guid, spell.AbilityId,
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

    /// <summary>
    /// Strips position/velocity/orientation from the field bitmap when the broadcast recipient
    /// is the same entity as the subject. Self-position flows via SPlayerStateAckPacket only;
    /// state fields (HP, Power, etc.) still ride this broadcast.
    /// </summary>
    public static GameEntityFields MaskSelfSuppression(GameEntityFields fields, ObjectGuid subjectGuid, ObjectGuid recipientGuid)
    {
        if (subjectGuid != recipientGuid) return fields;
        return fields & ~(GameEntityFields.Position | GameEntityFields.Velocity | GameEntityFields.Orientation);
    }

    private sealed class NoOpCreatureRespawner : ICreatureRespawner
    {
        public void Update(TimeSpan deltaTime) { }
        public void ScheduleRespawn(ICreature creature) { }
    }

    private sealed class PerPlayerBroadcastState
    {
        // Capacities sized for a typical instance (32 entities visible per player).
        // List<T> grows automatically if exceeded — this avoids early reallocation.
        public List<ObjectState> AddedObjects   { get; } = new(32);
        public List<ObjectState> UpdatedObjects { get; } = new(32);
    }
}
