using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.Loot;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.Social;
using Avalon.Network.Packets.State;
using Avalon.World.Abilities;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Characters;
using Avalon.World.ChunkLayouts;
using Avalon.World.Combat;
using Avalon.World.Configuration;
using Avalon.World.Creatures;
using Avalon.World.Creatures.Locomotion;
using Avalon.World.Entities;
using Avalon.World.Loot;
using Avalon.World.Maps.Navigation;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Pvp;
using Avalon.World.Quests;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Creatures;
using Avalon.World.Serialization;
using Avalon.World.Vendors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.World.Instances;

public class MapInstance : IMapInstance, IPortalSink, IGroundLootHost, IVendorHost, IAbilityArena, ICombatOutcomes,
    IDisposable
{
    private const float BroadcastInterval = 0.1f;

    private readonly Dictionary<ObjectGuid, ICharacter> _characters = [];
    private readonly Dictionary<ObjectGuid, IWorldConnection> _connections = [];
    private readonly ICorpseRemover _corpseRemover;
    private readonly Dictionary<ObjectGuid, ICreature> _creatures = [];
    private readonly ILogger<MapInstance> _logger;
    private readonly IMapNavigator _navigator;
    private readonly ICreatureLocomotion _locomotion;
    private readonly float _creatureAgentRadius;
    private readonly bool _crowdIncludesPlayers;
    private readonly InterestRange _interest;
    private readonly MeleeSlots _meleeSlots;
    private readonly IAbilityCastSystem _abilityCastSystem;
    private readonly EncounterRegistry _encounterRegistry;
    private readonly CombatService _combatService;
    private readonly UnitHitQuery _hits;
    private readonly ThreatBroadcastService _threatBroadcast;
    private readonly IWorld _world;
    private float _lastBroadcastTime;
    private readonly Dictionary<ObjectGuid, GameEntityFields> _frameDirtyFields = new(256);
    private readonly Dictionary<ObjectGuid, PerPlayerBroadcastState> _broadcastStates = [];

    /// <summary>
    /// The messages every player's state broadcast describes its view with (#640), reset for each player:
    /// its packets are serialized as they are created, so the next player can have the messages back.
    /// </summary>
    private readonly ObjectStatePool _statePool = new();
    private readonly List<PortalInstance> _portals = new();

    /// <summary>This tick's ability objects (projectiles), which the cast system fills and the broadcast reads.</summary>
    private readonly List<IWorldObject> _objectAbilities = new(64);
    private readonly ICharacterSaveScheduler? _saveScheduler;
    private readonly GroundLootStore _groundLoot = new();
    private readonly ILootRoller? _lootRoller;
    private readonly ILootAllocator? _lootAllocator;
    private readonly QuestService? _questService;

    /// <summary>Who shares a kill, and how its experience is split (2026-09-30). Null in tests built without one: solo rules.</summary>
    private readonly PartyService? _parties;

    /// <summary>
    /// Vendor stock (#432): one state per vendor creature that has had a shop open here. It lives as
    /// long as the instance, so for the persistent town a restart is what resets it.
    /// </summary>
    private readonly VendorStocks _vendors = new();

    private readonly TimeProvider _time;

    // Creature health scaling (2026-09-30), party instances only: set when someone enters or leaves, applied once at
    // the start of the next Update, so several changes in one tick make one rescale and one message.
    private double _healthFactor = 1d;
    private bool _healthFactorOwed;
    private int _presenceChanges;
    private string? _presenceChange;

    /// <summary>The seeded combat formula, for an instance whose world has no reference data loaded (tests).</summary>
    private static readonly Avalon.Domain.World.CombatFormula SeededFormula = Avalon.Database.World.Seeding.CombatSeed.Formula();
    private readonly PvpToggle _pvp;
    private readonly IQuestProgress _quests;

    /// <summary>
    /// Characters added since the last tick, owed a snapshot of the drops already on the ground. Sent
    /// at the start of the next Update rather than from AddCharacter: the handlers that move a
    /// character call TransferPlayer before they send the map transition, and the client must learn
    /// the new map before it learns what lies on it.
    /// </summary>
    private readonly HashSet<ObjectGuid> _lootSnapshotOwed = [];

    /// <summary>Characters that entered since the last tick and are owed their own PvP state (#164).</summary>
    private readonly HashSet<ObjectGuid> _pvpStateOwed = [];

    public MapInstance(
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider,
        IWorld world,
        MapTemplateId templateId,
        uint? ownerCharacterId,
        ChunkLayout layout,
        IMapNavigator navigator,
        int seed,
        MapType mapType = MapType.Normal,
        Func<ICreatureLocomotion, ICreatureLocomotion>? locomotion = null,
        PartyId? ownerPartyId = null)
    {
        _logger = loggerFactory.CreateLogger<MapInstance>();
        _world = world;
        InstanceId = Guid.NewGuid();
        TemplateId = templateId;
        MapType = mapType;
        OwnerCharacterId = ownerCharacterId;
        OwnerPartyId = ownerPartyId;
        AllowedCharacters = ownerCharacterId.HasValue ? new[] { ownerCharacterId.Value } : Array.Empty<uint>();
        Layout = layout;
        EntrySpawnWorldPos = layout.EntrySpawnWorldPos;
        Seed = seed;
        _navigator = navigator;
        _creatureAgentRadius = world.Configuration.CreatureAgentRadius;
        _crowdIncludesPlayers = world.Configuration.CrowdIncludesPlayers;
        _interest = new InterestRange(world.Configuration.InterestRadius, world.Configuration.InterestRemoveMargin);
        // The optional locomotion hook (#638) is for a harness or test that must measure or replace the
        // locomotion this instance would build; it is handed that one and returns the one to use.
        // Production passes none. Deliberately not on IMapInstance, the modding API.
        ICreatureLocomotion configured = CreateLocomotion(world.Configuration);
        _locomotion = locomotion is null ? configured : locomotion(configured);
        _meleeSlots = new MeleeSlots(world.Configuration.MeleeSlotCount, world.Configuration.MeleeSlotRadius);
        WarnIfMeleeSlotRadiusUnreachable(world.Configuration.MeleeSlotRadius);

        _corpseRemover = new CreatureCorpseRemover(this);

        // The container's clock, the one the loot allocator and the vendor handlers read (#432). It
        // falls back, so an instance built without one (tests) still ticks; production registers it.
        _time = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;

        // Empty from the moment it exists: an instance nobody ever enters (a party build finished after the party
        // disbanded) still expires, instead of staying live for good. AddCharacter clears it.
        LastEmptyAt = _time.GetUtcNow().UtcDateTime;

        // PvP (#164): the one toggle the handler, /pvp and every instance share. Production registers
        // it (WorldHostGraphShould); the fallback serves instances built without one (tests).
        _pvp = serviceProvider.GetService<PvpToggle>()
               ?? new PvpToggle(Options.Create(world.Configuration), _time);

        // Per-instance combat state. CombatConfig is a process-wide singleton (V1: defaults);
        // EncounterRegistry + CombatService are instance-scoped so encounters cannot bleed
        // between MapInstances.
        CombatConfig combatConfig = serviceProvider.GetRequiredService<CombatConfig>();
        _encounterRegistry = new EncounterRegistry(combatConfig, _time);
        // #506: every roll goes through the container's combat random, and each hit reads the current
        // combat formula once. Both fall back, so an instance built without them (tests) still fights.
        _combatService     = new CombatService(combatConfig, _encounterRegistry, this, _pvp, outcomes: this, time: _time,
            furyFromDamageTaken: world.Configuration.FuryFromDamageTaken,
            random: serviceProvider.GetService<ICombatRandom>(),
            formula: () => world.Data?.Combat?.Formula ?? SeededFormula);
        _threatBroadcast   = new ThreatBroadcastService(combatConfig, _time);

        // Shape scripts ask this for the living units their shape overlaps (#164).
        _hits = new UnitHitQuery(_characters, _creatures);

        // The cast system gets `this` as the IAbilityArena every ability script is built with
        // (#164): scripts route damage through CombatService.ApplyDamage. CombatService must be
        // assigned BEFORE this so any first-tick cast resolves through a non-null service.
        _abilityCastSystem = new InstanceAbilityCastSystem(loggerFactory, serviceProvider,
            serviceProvider.GetRequiredService<IScriptManager>(), this);

        // Optional so an instance built without one (tests) simply has no periodic save.
        _saveScheduler = serviceProvider.GetService<ICharacterSaveScheduler>();

        // Optional for the same reason: an instance built without them (tests) drops nothing.
        // Production registers both; WorldHostGraphShould proves it.
        _lootRoller = serviceProvider.GetService<ILootRoller>();
        _lootAllocator = serviceProvider.GetService<ILootAllocator>();
        _questService = serviceProvider.GetService<QuestService>();

        // Optional too: an instance built without it (tests) shares nothing, so every kill is its killer's alone.
        _parties = serviceProvider.GetService<PartyService>();

        // Vendor quest gates (#432). Falls back, so an instance built without it (tests) still
        // ticks; production registers it.
        _quests = serviceProvider.GetService<IQuestProgress>() ?? NoQuestProgress.Instance;
    }

    /// <summary>
    /// Ends the instance's ground state once the registry drops it. Nothing outside the instance holds
    /// it: a unit's broadcasts, hits, kills and departures reach the instance it is in directly, never
    /// through a static event (#546). Idempotent, since expiry can race a manual removal.
    /// </summary>
    public void Dispose()
    {
        // Drops are never persisted, and never despawn on a timer: an instance's disposal is the end
        // of every drop still on its ground.
        _groundLoot.Clear();
        _lootSnapshotOwed.Clear();
        _pvpStateOwed.Clear();
    }

    public Guid InstanceId { get; }
    public MapTemplateId TemplateId { get; }
    public MapType MapType { get; }
    public uint? OwnerCharacterId { get; }

    /// <summary>The party that owns this instance, or null (2026-09-30). World-side, init-only; a party instance has no owner character.</summary>
    public PartyId? OwnerPartyId { get; }
    public IReadOnlyList<uint> AllowedCharacters { get; }
    public int PlayerCount => _characters.Count;
    public DateTime? LastEmptyAt { get; private set; }
    public int Seed { get; }
    public string ConfigVersion => Layout?.ConfigVersion ?? string.Empty;
    public ChunkLayout? Layout { get; }
    public Vector3? EntrySpawnWorldPos { get; }
    public IReadOnlyList<PortalInstance> Portals => _portals;

    public IReadOnlyDictionary<ObjectGuid, ICharacter> Characters => _characters;

    /// <summary>
    /// Every connection whose character is in this instance. World-side, not on IMapInstance: the modding API
    /// must not reach other players' connections. Tick thread only.
    /// </summary>
    public IEnumerable<IWorldConnection> Connections => _connections.Values;
    public IReadOnlyDictionary<ObjectGuid, ICreature> Creatures => _creatures;
    public ICombatService CombatService => _combatService;
    public IHitQuery Hits => _hits;
    public ICreatureLocomotion Locomotion => _locomotion;
    public IMeleeSlots MeleeSlots => _meleeSlots;

    /// <summary>What every creature's maximum health is multiplied by now: 1 outside a party instance.</summary>
    public double HealthFactor => _healthFactor;

    /// <summary>1 + perExtraPlayer × (players − 1); 1 for one player or none.</summary>
    public static double HealthFactorFor(int presentPlayers, float perExtraPlayer) =>
        presentPlayers <= 1 ? 1d : 1d + perExtraPlayer * (double)(presentPlayers - 1);

    /// <summary>In a party instance, owes a rescale at the start of the next Update, naming who came or went.</summary>
    private void NotePresenceChange(ICharacter character, string verb)
    {
        if (OwnerPartyId is null)
            return;

        _healthFactorOwed = true;
        _presenceChanges++;
        _presenceChange = $"{character.Name} has {verb}.";
    }

    /// <summary>
    /// Rescales every living creature to the players present (alive or dead, a leave countdown included) and tells
    /// everyone here, once. Before the creature scripts, so this tick's fights read the new health.
    /// </summary>
    private void ApplyHealthFactor()
    {
        int changes = _presenceChanges;
        string? change = _presenceChange;
        _healthFactorOwed = false;
        _presenceChanges = 0;
        _presenceChange = null;

        int players = _characters.Count;
        double factor = HealthFactorFor(players, _world.Configuration.PartyHealthPerExtraPlayer);
        if (Math.Abs(factor - _healthFactor) < 1e-9)
            return;

        _healthFactor = factor;
        foreach (ICreature creature in _creatures.Values)
        {
            if (creature is Creature scaled)
                scaled.Rescale(factor);
        }

        int percent = (int)Math.Round(factor * 100, MidpointRounding.AwayFromZero);
        string text = (changes == 1 && change is not null ? change + " " : string.Empty)
                      + $"Creatures now have {percent}% health ({players} {(players == 1 ? "player" : "players")}).";
        DateTime now = _time.GetUtcNow().UtcDateTime;
        foreach (IWorldConnection connection in _connections.Values)
            connection.Send(SChatMessagePacket.System(text, now, connection.CryptoSession.Encrypt));
    }

    public bool IsExpired(TimeSpan expiry) =>
        LastEmptyAt.HasValue && (_time.GetUtcNow().UtcDateTime - LastEmptyAt.Value) >= expiry;

    public bool CanAcceptPlayer(ushort maxPlayers) => _characters.Count < maxPlayers;

    public void AddPortal(PortalInstance portal) => _portals.Add(portal);

    public IMapNavigator GetNavigatorForPosition(Vector3 position) => _navigator;

    /// <summary>
    /// Chooses the locomotion implementation per <see cref="GameConfiguration.CreatureLocomotion" />.
    /// <see cref="CrowdLocomotion" /> needs a non-null baked <see cref="DtNavMesh" />, but the
    /// navigator handed to this instance is only an <see cref="IMapNavigator" /> — tests substitute
    /// it, and even a real <see cref="MapNavigator" /> can have nothing baked into it yet — so a
    /// configured crowd degrades to <see cref="WaypointLocomotion" /> instead of throwing out of the
    /// constructor. Creatures that cannot move at all are worse than creatures that move badly.
    /// </summary>
    private ICreatureLocomotion CreateLocomotion(GameConfiguration config)
    {
        if (config.CreatureLocomotion != CreatureLocomotionMode.Crowd)
            return new WaypointLocomotion(GetNavigatorForPosition);

        if (_navigator is MapNavigator { NavMesh: { } navMesh })
            return new CrowdLocomotion(navMesh, config.CreatureAgentRadius, _logger);

        // Creatures that cannot move at all are worse than creatures that move badly.
        _logger.LogWarning(
            "Crowd locomotion was configured but map {MapId} has no baked navmesh; " +
            "falling back to waypoint locomotion for this instance",
            TemplateId);
        return new WaypointLocomotion(GetNavigatorForPosition);
    }

    /// <summary>
    /// Warns when the configured <see cref="GameConfiguration.MeleeSlotRadius" /> places attackers'
    /// standing positions beyond <see cref="CreatureCombatScript.AttackRange" />: a creature that
    /// walks to its claimed slot and arrives there is then standing outside attack range and never
    /// attacks from it, so the ring fills up and every attacker in it deals zero damage forever, with
    /// no other symptom than mobs standing still around their target. Compared against bare
    /// <see cref="CreatureCombatScript.AttackRange" /> rather than the full effective reach
    /// (AttackRange + the selected locomotion's arrival tolerance + its float-noise margin, see
    /// CreatureCombatScript.AttackRangeArrivalMargin): the full reach would make the same
    /// MeleeSlotRadius warn or not depending on which locomotion this instance ended up with
    /// (including CreateLocomotion's own navmesh-missing fallback above), which is an unrelated
    /// operational detail an operator reading this warning should not have to account for. Comparing
    /// against AttackRange alone warns slightly earlier than strictly necessary but never misses a
    /// real failure, and — importantly — leaves the shipped default (MeleeSlotRadius == AttackRange
    /// == 1.5) silent, since it is a strict "greater than", not "greater than or equal to".
    /// </summary>
    private void WarnIfMeleeSlotRadiusUnreachable(float meleeSlotRadius)
    {
        if (meleeSlotRadius <= CreatureCombatScript.AttackRange)
            return;

        _logger.LogWarning(
            "MeleeSlotRadius {MeleeSlotRadius} on map {MapId} exceeds the creature attack range of " +
            "{AttackRange}; creatures will walk to their claimed melee slot, arrive there, and then " +
            "stand outside attack range forever, dealing zero damage. Lower MeleeSlotRadius to at " +
            "most the attack range above to fix it.",
            meleeSlotRadius, TemplateId, CreatureCombatScript.AttackRange);
    }

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

        // #611: a character arriving from another instance (entering a map, respawning at a town, or
        // re-entering this one) starts over. The client keeps every object it was told about until it
        // is told the object is gone, so it is told now, once, to drop everything it knew there, its
        // own character included; this goes out ahead of the map transition the caller sends next.
        // Its first tick here then adds, in full, itself and everything it can see. A character new to
        // the world has been told about nothing, and is sent nothing here.
        if (connection.Character is CharacterEntity arriving)
        {
            IReadOnlyList<ObjectGuid> forgotten = arriving.CharacterGameState.Reset();
            if (forgotten.Count > 0)
                connection.Send(SInstanceStateRemovePacket.Create(forgotten, connection.CryptoSession.Encrypt));

            // #526: every instance move comes through here (map entry, respawn at a town, a portal), and
            // each one starts with no Fury. Mana and Energy are kept.
            arriving.ResetFury();
        }

        _characters[connection.Character!.Guid] = connection.Character;
        _connections[connection.Character.Guid] = connection;
        _broadcastStates[connection.Character.Guid] = new PerPlayerBroadcastState();
        _lootSnapshotOwed.Add(connection.Character.Guid);
        _pvpStateOwed.Add(connection.Character.Guid);
        LastEmptyAt = null;

        NotePresenceChange(connection.Character, "entered");
    }

    public void RemoveCharacter(IWorldConnection connection)
    {
        ICharacter character = connection.Character!;
        ObjectGuid guid = character.Guid;

        // A cast in progress ends here, while the character is still a member, so everyone here sees
        // the interrupt (#164). Left queued, it would never complete once this instance empties, and
        // Casting would refuse every cast the character tried anywhere else. Contained, so a failure
        // cannot keep the character a member.
        try
        {
            _abilityCastSystem.CancelCasts(character);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Cancelling the casts of {CharacterGuid} as it left instance {InstanceId} failed",
                guid, InstanceId);
        }

        // Its scripts still running here go too (#541): a projectile in flight would otherwise land
        // for a caster who is gone, giving threat, kill credit and loot to someone no longer here. A
        // dropped projectile leaves the next tick's world objects, so every watcher is sent one remove.
        try
        {
            _abilityCastSystem.CancelScriptsOf(character);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Dropping the scripts of {CharacterGuid} as it left instance {InstanceId} failed",
                guid, InstanceId);
        }

        // Membership next. These cannot throw, and once they are gone the tick no longer updates,
        // broadcasts or periodically saves the character, whatever the hooks below do. A disconnect
        // hook that threw ahead of them used to leave a despawned character live in the instance.
        _characters.Remove(guid);
        _connections.Remove(guid);
        _broadcastStates.Remove(guid);
        _lootSnapshotOwed.Remove(guid);
        _pvpStateOwed.Remove(guid);
        NotePresenceChange(character, "left");

        if (_characters.Count == 0)
        {
            LastEmptyAt = _time.GetUtcNow().UtcDateTime;
            _logger.LogInformation("Instance {InstanceId} (map {TemplateId}) is now empty", InstanceId, TemplateId);
        }

        try
        {
            TellScriptsCharacterLeft(character);
        }
        finally
        {
            _threatBroadcast.Forget(connection);

            // Idempotent and safe to call unconditionally: a no-op under WaypointLocomotion, and a
            // no-op if this character was never synced as a player agent in the first place (flag off,
            // or the disconnect races the per-tick sync in Update below).
            _locomotion.RemovePlayer(guid);
        }
    }

    /// <summary>
    /// Each of this instance's creature scripts hears that <paramref name="character" /> left (#546).
    /// Contained per script, so one that throws cannot stop the others or the removal. Over a copy, so
    /// a script that adds or removes a creature does not break the walk.
    /// </summary>
    private void TellScriptsCharacterLeft(ICharacter character)
    {
        foreach (ICreature creature in _creatures.Values.ToArray())
        {
            try
            {
                creature.Script?.OnCharacterLeft(character);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "The script of creature {CreatureGuid} failed on {CharacterGuid} leaving instance {InstanceId}",
                    creature.Guid, character.Guid, InstanceId);
            }
        }
    }

    public void AddCreature(ICreature creature)
    {
        _creatures[creature.Guid] = creature;
        _locomotion.Register(creature, _creatureAgentRadius);

        // A creature spawned later in a party instance starts at the health the players here make.
        if (_healthFactor != 1d && creature is Creature scaled)
            scaled.Rescale(_healthFactor);
    }

    public void RemoveCreature(ICreature creature)
    {
        _creatures.Remove(creature.Guid);

        // A wind-up in progress ends here, out loud, and a projectile in flight is dropped (#163): a corpse
        // removed or a script hot reloaded mid-cast would otherwise fire it later, from a creature that is gone
        // or from its old script.
        // Contained, so a failure cannot keep the creature a member.
        try
        {
            _abilityCastSystem.CancelCasts(creature);
            _abilityCastSystem.CancelScriptsOf(creature);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Cancelling the casts of creature {CreatureGuid} as it left instance {InstanceId} failed",
                creature.Guid, InstanceId);
        }

        // Stop BEFORE Unregister, exactly as CreatureKilled does and for the same reason: Stop is
        // what brings the creature to rest (MoveState.Idle, zero Velocity), and both locomotion
        // implementations no-op on an unregistered creature. Reversed, a creature removed mid-walk
        // (script hot reload runs through here — World.ApplyScriptsHotReload calls
        // RemoveCreature/AddCreature around every reload) would keep whatever MoveState and Velocity
        // it last had, and the client would extrapolate a creature that never stops moving.
        _locomotion.Stop(creature);
        _locomotion.Unregister(creature);

        // Despawn runs through here without ever consulting the creature's script, so this is
        // the only place a slot held by a despawning creature can be released. Safe unconditionally
        // — a no-op when the creature never claimed one.
        _meleeSlots.ReleaseClaimant(creature.Guid);
    }

    public bool QueueAbility(IUnit caster, AbilityAim aim, IAbility ability) =>
        _abilityCastSystem.QueueAbility(caster, aim, ability);

    public bool RunInstantAbility(IUnit caster, AbilityAim aim, IAbility ability) =>
        _abilityCastSystem.RunInstant(caster, aim, ability);

    /// <summary>
    /// Sends a hit to everyone who hears it (#532). A creature script's broadcast, made during the hit's
    /// OnHit, is marked with how the hit went (#506); any other is None.
    /// </summary>
    public void BroadcastUnitHit(IUnit attacker, IUnit target, uint currentHealth, uint damage) =>
        BroadcastUnitHit(attacker, target, currentHealth, damage, _hitInFlight);

    private void BroadcastUnitHit(IUnit attacker, IUnit target, uint currentHealth, uint damage, HitResult result)
    {
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, attacker.Guid, target.Guid, attacker.Position, target.Position))
            {
                continue;
            }

            connection.Send(SUnitDamagePacket.Create(attacker.Guid, target.Guid.RawValue,
                currentHealth, damage, connection.CryptoSession.Encrypt, result));
        }
    }

    // How the hit whose creature script is running now went (#506); see ICombatOutcomes.HitInFlight.
    // Tick thread only, like every hit.
    private HitResult _hitInFlight;

    HitResult ICombatOutcomes.HitInFlight { set => _hitInFlight = value; }

    public void BroadcastUnitStartCast(IUnit caster, IAbility ability, uint castId, AbilityFootprint? footprint)
    {
        // Heard near the caster or near where it will land (#648), so a watcher standing in the telegraph sees it.
        Vector3? centre = footprint?.Centre;
        AbilityFootprintDto? dto = footprint?.ToDto();
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, caster.Guid, null, caster.Position, centre))
            {
                continue;
            }

            // #627: the time the cast system just set, haste included, so every cast bar ends when the cast does.
            connection.Send(SUnitStartCastPacket.Create(caster.Guid, ability.CastTimeTimer,
                ability.AbilityId.Value, castId, dto, connection.CryptoSession.Encrypt));
        }
    }

    // The cast whose script the cast system is firing now (#648); see IAbilityArena.CastInFlight.
    private uint _castInFlight;

    uint IAbilityArena.CastInFlight { set => _castInFlight = value; }

    public void BroadcastAbilityFired(IUnit caster, IAbility ability, AbilityFootprint footprint)
    {
        AbilityFootprintDto dto = footprint.ToDto();
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, caster.Guid, null, footprint.Origin, footprint.Centre))
            {
                continue;
            }

            connection.Send(SAbilityFiredPacket.Create(caster.Guid.RawValue, ability.AbilityId.Value, _castInFlight,
                dto, connection.CryptoSession.Encrypt));
        }
    }

    public void BroadcastUnitDeath(IUnit unit, IUnit? killer)
    {
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, unit.Guid, killer?.Guid, unit.Position, null))
            {
                continue;
            }

            connection.Send(SUnitDeathPacket.Create(unit.Guid, killer?.Guid,
                connection.CryptoSession.Encrypt));
        }
    }

    public void BroadcastUnitRevive(IUnit unit, Vector3 position, uint health)
    {
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, unit.Guid, null, position, null))
            {
                continue;
            }

            connection.Send(SUnitRevivePacket.Create(unit.Guid, position, health,
                connection.CryptoSession.Encrypt));
        }
    }

    /// <summary>
    /// Whether the connection of character <paramref name="guid" /> receives a one-shot effect broadcast
    /// (#532): involved in it, or within Game:InterestRadius of one of its points. A connection
    /// with no character is near nothing.
    /// </summary>
    private bool Hears(ObjectGuid guid, IWorldConnection connection, ObjectGuid involved, ObjectGuid? alsoInvolved,
        Vector3 point, Vector3? alsoPoint) =>
        EffectAudience.Receives(guid, connection.Character?.Position ?? Unplaced, _interest.Radius,
            involved, alsoInvolved, point, alsoPoint);

    /// <summary>The position of a connection with no character: not finite, so never near an effect.</summary>
    private static readonly Vector3 Unplaced = new(float.NaN, float.NaN, float.NaN);

    public GroundLootStore Drops => _groundLoot;

    public VendorStocks Vendors => _vendors;

    public void BroadcastLootDespawned(IReadOnlyCollection<ObjectGuid> lootGuids)
    {
        foreach ((ObjectGuid _, IWorldConnection connection) in _connections)
        {
            connection.Send(SLootDespawnedPacket.Create(lootGuids, connection.CryptoSession.Encrypt));
        }
    }

    /// <summary>
    /// Rolls, allocates and places a dying creature's drops and tells everyone here. Tick thread: CreatureKilled is
    /// called by this instance's combat service inside combat and ability processing. Reads the Loot, Items and
    /// Quests areas as they are now, so a reload applies to the next kill. Each table drop is allocated on its own,
    /// among the characters that share the kill (2026-09-30); each quest drop (#433) belongs to the member it was
    /// rolled for, for good. All of them are placed in one ring.
    /// </summary>
    private void DropLoot(ICreature creature, IReadOnlyList<ICharacter> eligible)
    {
        IReadOnlyList<(RolledDrop Drop, uint Owner)> questDrops = RollQuestDrops(creature, eligible);
        bool rollsTable = _lootRoller is not null && _lootAllocator is not null && creature.Metadata is CreatureTemplate;
        if (!rollsTable && questDrops.Count == 0)
        {
            return;
        }

        // A throw here would skip the experience award after it and escape into the combat code
        // that killed the creature. One bad table costs one kill's loot, nothing more.
        try
        {
            IReadOnlyList<RolledDrop> table = rollsTable
                ? _lootRoller!.Roll((CreatureTemplate)creature.Metadata, _world.Data.Loot, _world.Data.ItemTemplates)
                : [];
            if (table.Count == 0 && questDrops.Count == 0)
            {
                return;
            }

            var rolled = new List<RolledDrop>(table.Count + questDrops.Count);
            rolled.AddRange(table);
            foreach ((RolledDrop drop, uint _) in questDrops)
                rolled.Add(drop);

            ILootAllocator? allocator = _lootAllocator;
            int tableCount = table.Count;
            IReadOnlyList<GroundLoot> drops = LootPlacement.Place(
                creature.Position, rolled,
                i => i < tableCount
                    ? allocator!.Allocate(OwnerCharacterId, OwnerPartyId, eligible)
                    : new LootAllocation(questDrops[i - tableCount].Owner, DateTime.MaxValue),
                GetNavigatorForPosition(creature.Position), IObject.GenerateId);

            foreach (GroundLoot drop in drops)
            {
                _groundLoot.Add(drop);
            }

            SendLootSpawned(_connections.Values, drops);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not drop loot for creature {CreatureGuid} (template {TemplateId}); the kill still counts",
                creature.Guid, creature.Metadata.Id.Value);
        }
    }

    /// <summary>The quest drops a kill rolls (#433). Contained: a throw costs this kill's quest drops, nothing else.</summary>
    private IReadOnlyList<(RolledDrop Drop, uint Owner)> RollQuestDrops(ICreature creature, IReadOnlyList<ICharacter> eligible)
    {
        if (_questService is null || eligible.Count == 0)
        {
            return [];
        }

        try
        {
            return _questService.RollQuestDrops(creature, eligible);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Rolling quest drops for the kill of {CreatureGuid} in instance {InstanceId} failed",
                creature.Guid, InstanceId);
            return [];
        }
    }

    private void SendOwedLootSnapshots()
    {
        if (_lootSnapshotOwed.Count == 0)
        {
            return;
        }

        if (_groundLoot.Count > 0)
        {
            foreach (ObjectGuid guid in _lootSnapshotOwed)
            {
                if (_connections.TryGetValue(guid, out IWorldConnection? connection))
                {
                    SendLootSpawned([connection], _groundLoot.All);
                }
            }
        }

        _lootSnapshotOwed.Clear();
    }

    /// <summary>
    /// #164: a character entering this instance learns its own flag and any timer left. A timer that
    /// ran out while it was offline is expired first, so it hears one (false, 0), not a stale "on".
    /// </summary>
    private void SendOwedPvpStates()
    {
        foreach (ObjectGuid guid in _pvpStateOwed)
        {
            if (_connections.TryGetValue(guid, out IWorldConnection? owed) && owed.Character is CharacterEntity entity)
            {
                _pvp.ExpireIfDue(entity);
                _pvp.Send(owed, entity);
            }
        }

        _pvpStateOwed.Clear();
    }

    private static void SendLootSpawned(IEnumerable<IWorldConnection> recipients, IReadOnlyCollection<GroundLoot> drops)
    {
        // Built once; each connection serializes it under its own session key.
        List<LootDropDto> dtos = drops.Select(LootDropMapper.ToDto).ToList();

        foreach (IWorldConnection connection in recipients)
        {
            connection.Send(SLootSpawnedPacket.Create(dtos, connection.CryptoSession.Encrypt));
        }
    }

    public void Update(TimeSpan deltaTime)
    {
        if (_characters.Count == 0)
        {
            // Nothing ticks while nobody is here, but a projectile that finished just before the last
            // character left would wait for a broadcast that never comes, and show frozen to the next
            // player to enter (#164). Nobody is left to send its final state to, so drop it now.
            _abilityCastSystem.DropFinished();
            return;
        }

        if (_healthFactorOwed)
            ApplyHealthFactor();

        _lastBroadcastTime += (float)deltaTime.TotalSeconds;

        // Step 0: drops already on the ground, for characters that entered since the last tick. First,
        // before any packet is processed, so a kill later in this tick reaches them once, through the
        // kill broadcast, rather than twice.
        SendOwedLootSnapshots();
        SendOwedPvpStates();

        // Step 1: remove the corpses whose BodyRemoveTimer has run out. Creatures do not respawn.
        _corpseRemover.Update(deltaTime);

        // Step 2: Process character packets
        UpdateCharacters(deltaTime);

        // Step 2b: Vendors (#432). After the packets, so this tick's trades are in the lists. The
        // pass refills due stock, adopts a /reload vendors, and sends each connection whose shop is
        // open the one list it is owed. It is skipped until someone opens a shop here, so an
        // instance with no vendor state never reads vendor data.
        if (_vendors.Count > 0)
            RunVendorPass();

        // #612: one list for the life of the instance, refilled each tick, so a tick allocates none.
        _objectAbilities.Clear();

        // Step 3: abilities, combat and threat.
        UpdateCombat(deltaTime, _objectAbilities);

        // Step 4: creature scripts, then the locomotion that executes what they decided.
        UpdateCreatures(deltaTime);

        // Step 5a: Snapshot dirty fields — ONLY on broadcast ticks.
        SnapshotDirtyFields(_objectAbilities);

        // Step 5b and 6: visibility, then the state broadcast, per character.
        BroadcastState(_objectAbilities);

        if (_lastBroadcastTime >= BroadcastInterval)
        {
            _lastBroadcastTime = 0;
        }
    }

    /// <summary>Step 2 of <see cref="Update" />: each character's packets, its own tick, its PvP timer and its periodic save.</summary>
    private void UpdateCharacters(TimeSpan deltaTime)
    {
        foreach ((ObjectGuid guid, ICharacter character) in _characters)
        {
            IWorldConnection connection = _connections[guid];
            connection.UpdateMap();
            character.Update(deltaTime);

            // #164: the off timer is checked every tick, ahead of the periodic save so it carries the
            // change. A running timer a player-on-player hit moved is re-sent, so the countdown is exact.
            if (character is CharacterEntity pvpEntity
                && (_pvp.ExpireIfDue(pvpEntity) || PvpToggle.CountdownOwed(pvpEntity)))
                _pvp.Send(connection, pvpEntity);

            // Periodic save (spec #459 D4): the scheduler decides whether this is the character's tick.
            if (character is CharacterEntity entity)
                _saveScheduler?.Tick(connection, entity, deltaTime);
        }
    }

    /// <summary>Step 3 of <see cref="Update" />: the cast system, then the combat service, then the threat mirror.</summary>
    private void UpdateCombat(TimeSpan deltaTime, List<IWorldObject> objectAbilities)
    {
        // Step 3: Ability cast system update
        _abilityCastSystem.Update(deltaTime, objectAbilities);

        // Step 3b: Tick combat service — decays threat, ends stale encounters.
        _combatService.Update(deltaTime);

        // Step 3c: Mirror threat lists for each player's currently-targeted hostile.
        // Throttled (250 ms / 5 % delta) inside the service; iterating _connections.Values
        // here is safe because no inbound packet handler dequeued above mutates _connections
        // (target-unit just stores a ulong on the connection itself).
        _threatBroadcast.Tick(_connections.Values, _creatures, _combatService);
    }

    /// <summary>Step 4 of <see cref="Update" />: creature scripts, the player sync, then the locomotion.</summary>
    private void UpdateCreatures(TimeSpan deltaTime)
    {
        // Step 4: Update creature scripts. A creature's cooldowns run down inside its combat script (#163),
        // the one thing that casts them.
        foreach (ICreature creature in _creatures.Values)
        {
            creature.Script?.Update(deltaTime);
        }

        // Players are told to the crowd, never asked: PlayerInputHandler already decided where they
        // are earlier in this tick. Off unless configured, because it makes body-blocking real. The
        // `is IPlayerAwareLocomotion` check (rather than dispatching for every character whatever the
        // locomotion) means this costs nothing beyond the flag check and one type test when the flag
        // is off or the instance is running WaypointLocomotion — no allocation, no iteration. The
        // interface, not CrowdLocomotion itself, so a decorator around the crowd gets this very sync.
        if (_crowdIncludesPlayers && _locomotion is IPlayerAwareLocomotion crowd)
        {
            foreach ((ObjectGuid guid, ICharacter character) in _characters)
                crowd.SyncPlayer(guid, character.Position);
        }

        // After the scripts, because they decide destinations and this executes them — reversed, every
        // creature acts on last tick's decision, and a destination chosen this tick is not walked until
        // the next one. Pinned by MapInstanceLocomotionShould.Tick_The_Locomotion_After_The_Creature_Scripts,
        // which is the only thing in the suite that fails if these two are swapped. Player positions are
        // already current: input was processed in connection.UpdateMap() earlier in this same tick.
        _locomotion.Update(deltaTime);
    }

    /// <summary>Step 5a of <see cref="Update" />.</summary>
    private void SnapshotDirtyFields(List<IWorldObject> objectAbilities)
    {
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
    }

    /// <summary>Steps 5b and 6 of <see cref="Update" />: every character's visibility first, then every broadcast.</summary>
    private void BroadcastState(List<IWorldObject> objectAbilities)
    {
        // Step 5b: Update entity visibility state per character, each by its own interest range (#593)
        // The replication state is World-side (#612), so a character that is not a CharacterEntity is
        // seen by the others but is sent nothing itself.
        foreach (ICharacter character in _characters.Values)
        {
            if (character is CharacterEntity entity)
                entity.CharacterGameState.Update(entity.Guid, entity.Position, _interest, _creatures,
                    _characters, objectAbilities, _frameDirtyFields);
        }

        // Step 6: Broadcast instance state to each character
        foreach (ICharacter character in _characters.Values)
        {
            if (character is CharacterEntity entity)
                BroadcastStateTo(entity);
        }
    }

    /// <summary>
    /// The vendor pass (#432), run by <see cref="Update" /> on every tick while this instance keeps
    /// stock, whether or not a shop is open: a restock timer a reload left owed starts on the next
    /// tick, not at the next sale. It reads the static data once, so the reconcile, the restock and
    /// every list work from one catalog, the same one the vendor handlers read on this tick; and it
    /// takes "now" from the container's TimeProvider, the clock the handlers take sales at. It walks
    /// the stock and the connections with struct enumerators and sends nothing unless something
    /// changed (a stock count, a restock, a /reload vendors, a /reload items, or a buyback), so a
    /// quiet pass allocates nothing. Tick thread only. Public rather than internal so
    /// the unit-test assembly can pin the allocation without an InternalsVisibleTo handshake.
    /// </summary>
    public void RunVendorPass()
    {
        StaticData data = _world.Data;
        _vendors.Update(_time.GetUtcNow().UtcDateTime, data.Vendors, data.ItemTemplates);

        foreach (IWorldConnection connection in _connections.Values)
            VendorListBuilder.SendIfOwed(connection, _vendors, data, _quests);

        _vendors.ClearChanged();
    }

    private void BroadcastStateTo(CharacterEntity character)
    {
        IWorldConnection connection = _connections[character.Guid];
        PerPlayerBroadcastState state = _broadcastStates[character.Guid];

        state.AddedObjects.Clear();
        state.UpdatedObjects.Clear();
        _statePool.Reset();

        // Indexed rather than walked with foreach, which boxes an interface's enumerator (#640).
        IReadOnlyList<ObjectGuid> newObjects = character.CharacterGameState.NewObjects;
        for (int i = 0; i < newObjects.Count; i++)
        {
            ObjectState? added = DescribeNewObject(newObjects[i], character.Guid);

            if (added is not null)
            {
                state.AddedObjects.Add(added);
            }
        }

        IReadOnlyList<(ObjectGuid Guid, GameEntityFields Fields)> updatedObjects =
            character.CharacterGameState.UpdatedObjects;
        for (int i = 0; i < updatedObjects.Count; i++)
        {
            ObjectState? updated = DescribeUpdatedObject(updatedObjects[i], character.Guid);

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
            connection.Send(SInstanceStateRemovePacket.Create(
                character.CharacterGameState.RemovedObjects, connection.CryptoSession.Encrypt));
        }
    }

    /// <summary>
    /// An entity the recipient has not seen before, described in full. Null when the entity
    /// has left the instance between being noticed and being described. Portals are never tracked, so
    /// never described here: a client gets them with the map, in SChunkLayoutPacket.Portals (#612).
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
                    MaskSelfSuppression(GameEntityFields.All, guid, recipientGuid),
                    _statePool);

            case ObjectType.Creature:
                if (!_creatures.TryGetValue(guid, out ICreature? addedCreature))
                    return null;
                return ObjectStateWriter.From(addedCreature, GameEntityFields.All, _statePool);

            case ObjectType.SpellProjectile:
                IWorldObject? addedAbility = _abilityCastSystem.GetAbility(guid);
                return addedAbility is null ? null : ObjectStateWriter.From(addedAbility, _statePool);

            default:
                _logger.LogWarning("Unknown object type {ObjectType} on NewObjects serialization", guid.Type);
                return null;
        }
    }

    /// <summary>
    /// A change to an entity the recipient already has. Null when the entity has left the
    /// instance.
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
                    MaskSelfSuppression(GameEntityFields.CharacterUpdate, updatedObject.Guid, recipientGuid),
                    _statePool);

            case ObjectType.Creature:
                if (!_creatures.TryGetValue(updatedObject.Guid, out ICreature? updatedCreature))
                    return null;
                // The routine selection, plus two members only when they changed: the death state (#672),
                // so a creature's death goes out on the broadcast that carries its 0 health, and the
                // maximum health (2026-09-30), so a party rescale reaches every client already watching
                // along with the kept share of its current health. The routine updates pay for neither.
                return ObjectStateWriter.From(updatedCreature,
                    GameEntityFields.CreatureUpdate
                    | (updatedObject.Fields & (GameEntityFields.IsDead | GameEntityFields.Health)), _statePool);

            case ObjectType.SpellProjectile:
                IWorldObject? updatedAbility = _abilityCastSystem.GetAbility(updatedObject.Guid);
                return updatedAbility is null
                    ? null
                    : ObjectStateWriter.From(updatedAbility, updatedObject.Fields, _statePool);

            default:
                _logger.LogWarning("Unknown object type {ObjectType} on UpdatedObjects serialization",
                    updatedObject.Guid.Type);
                return null;
        }
    }

    public void BroadcastAttackAnimation(IUnit attacker, IAbility? spell)
    {
        if (!_creatures.ContainsKey(attacker.Guid) && !_characters.ContainsKey(attacker.Guid))
        {
            return;
        }

        ushort animationId = ResolveBroadcastAnimationId(spell);
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, attacker.Guid, null, attacker.Position, null))
            {
                continue;
            }

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

    public void BroadcastFinishCast(IUnit attacker, IAbility spell) => BroadcastFinishCast(attacker, spell, 0u);

    public void BroadcastFinishCast(IUnit attacker, IAbility spell, uint castId)
    {
        if (!_creatures.ContainsKey(attacker.Guid) && !_characters.ContainsKey(attacker.Guid))
        {
            return;
        }

        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, attacker.Guid, null, attacker.Position, null))
            {
                continue;
            }

            connection.Send(SUnitFinishCastPacket.Create(attacker.Guid, spell.AbilityId, castId,
                connection.CryptoSession.Encrypt));
        }
    }

    public void BroadcastInterruptedCast(IUnit attacker, IAbility spell) => BroadcastInterruptedCast(attacker, spell, 0u);

    public void BroadcastInterruptedCast(IUnit attacker, IAbility spell, uint castId)
    {
        if (!_creatures.ContainsKey(attacker.Guid) && !_characters.ContainsKey(attacker.Guid))
        {
            return;
        }

        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, attacker.Guid, null, attacker.Position, null))
            {
                continue;
            }

            connection.Send(SCharacterInterruptedCastPacket.Create(attacker.Guid, spell.AbilityId, castId,
                connection.CryptoSession.Encrypt));
        }
    }

    /// <summary>
    /// How much of a kill's experience a player at <paramref name="playerLevel" /> earns on a map banded
    /// <paramref name="bandMin" />-<paramref name="bandMax" />. 1.0 inside the band, decaying per level
    /// outside it, symmetrically and with no grace.
    /// </summary>
    /// <remarks>
    /// A map with either bound unset is unbanded and scales nothing. Both bounds are nullable on
    /// <c>MapTemplate</c>, and treating a missing one as 0 would wipe out every award on that map.
    /// Public rather than internal so the unit-test assembly can call it without an
    /// InternalsVisibleTo handshake — the same reasoning as ChunkLayoutSourceResolver's test ctor.
    /// </remarks>
    public static double BandScale(ushort playerLevel, ushort? bandMin, ushort? bandMax, float decay)
    {
        if (bandMin is null || bandMax is null)
        {
            return 1.0;
        }

        int levelsOut = playerLevel < bandMin.Value ? bandMin.Value - playerLevel
                      : playerLevel > bandMax.Value ? playerLevel - bandMax.Value
                      : 0;

        return levelsOut == 0 ? 1.0 : Math.Pow(decay, levelsOut);
    }

    /// <summary>
    /// The wounded character is told its own damage first, then everyone here is sent the hit. Only for
    /// a character in this instance, so nobody sees damage numbers from a fight elsewhere.
    /// </summary>
    void ICombatOutcomes.CharacterDamaged(CharacterEntity character, IUnit attacker, uint damage, AbilityId? abilityId,
        HitResult result)
    {
        if (_connections.TryGetValue(character.Guid, out IWorldConnection? connection))
        {
            connection.Send(SCharacterDamagePacket.Create(attacker.Guid.RawValue, character.Guid.RawValue,
                character.CurrentHealth, damage, abilityId?.Value, connection.CryptoSession.Encrypt, result));
        }

        if (_characters.ContainsKey(character.Guid))
        {
            BroadcastUnitHit(attacker, character, character.CurrentHealth, damage, result);
        }
    }

    /// <summary>
    /// A dodge (#506) is sent as a hit of 0 marked Dodged: to the character dodging, as its own damage
    /// packet, and to everyone who hears it, by the same radius as a hit (#532). Only for a unit in this
    /// instance, as a hit is.
    /// </summary>
    void ICombatOutcomes.HitDodged(IUnit attacker, IUnit target, AbilityId? abilityId)
    {
        if (target is CharacterEntity character)
        {
            ((ICombatOutcomes)this).CharacterDamaged(character, attacker, 0, abilityId, HitResult.Dodged);
            return;
        }

        if (target is ICreature && _creatures.ContainsKey(target.Guid))
        {
            BroadcastUnitHit(attacker, target, target.CurrentHealth, 0, HitResult.Dodged);
        }
    }

    /// <summary>
    /// A heal that restored health (#506), to everyone who hears it (#532): the healer, the target, and whoever
    /// stands within the interest radius of the target. Only for a target in this instance, as a hit is.
    /// </summary>
    void ICombatOutcomes.UnitHealed(IUnit healer, IUnit target, uint restored, AbilityId? abilityId, HitResult result)
    {
        if (!_characters.ContainsKey(target.Guid) && !_creatures.ContainsKey(target.Guid))
        {
            return;
        }

        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, healer.Guid, target.Guid, target.Position, null))
            {
                continue;
            }

            connection.Send(SUnitHealedPacket.Create(healer.Guid.RawValue, target.Guid.RawValue, restored,
                target.CurrentHealth, abilityId?.Value, result, connection.CryptoSession.Encrypt));
        }
    }

    void ICombatOutcomes.CreatureKilled(ICreature creature, IUnit killer)
    {
        if (!_creatures.ContainsKey(creature.Guid))
        {
            return;
        }

        // Taken first, once, while the creature is still in its encounter (spec 2026-09-30 section 4). Contained: a
        // throw here must not leave a creature at 0 health with its script and no corpse teardown. Nobody is eligible
        // then: nobody gains experience from it, and a party instance's drops are free for all.
        IReadOnlyList<ICharacter> eligible;
        try
        {
            eligible = EligibleFor(creature, killer);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Working out who shares the kill of {CreatureGuid} in instance {InstanceId} failed",
                creature.Guid, InstanceId);
            eligible = [];
        }

        creature.Script = null;
        TearDownCorpse(creature);
        _corpseRemover.ScheduleRemoval(creature);

        // #433: kill credit for everyone who shares the kill, before the drops, which read the same quests.
        CreditQuests(creature, eligible);

        // Whatever killed it: loot does not depend on the killer being a character. A solo instance's drops
        // still go to its owner and a town's are free for all; only a party instance draws among the eligible.
        DropLoot(creature, eligible);
        AwardExperience(creature, eligible);
    }

    /// <summary>
    /// Quest credit for a kill (#433). Contained, as the eligibility is: a throw costs this kill's quest credit, never
    /// the corpse teardown, the loot or the experience after it.
    /// </summary>
    private void CreditQuests(ICreature creature, IReadOnlyList<ICharacter> eligible)
    {
        if (_questService is null || eligible.Count == 0)
            return;

        try
        {
            _questService.CreatureKilled(creature, eligible);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Quest credit for the kill of {CreatureGuid} in instance {InstanceId} failed; the kill still counts",
                creature.Guid, InstanceId);
        }
    }

    /// <summary>
    /// The characters that share this kill: the killer alone outside a party, or every present, eligible member of
    /// its party. Empty when the killer is not a character or is in a leave countdown.
    /// </summary>
    private IReadOnlyList<ICharacter> EligibleFor(ICreature creature, IUnit killer)
    {
        ICharacter? character = killer as ICharacter;
        Party? party = character is null ? null : _parties?.PartyOf(character.Guid.Id);
        Func<uint, bool> inCountdown = _parties is null ? static _ => false : _parties.InCountdown;

        return PartyEligibility.For(character, party, _characters, inCountdown,
            _combatService.GetEncounterFor(creature), creature.Position, _world.Configuration.PartyEligibilityRange);
    }

    /// <summary>
    /// Brings a creature that has just died to rest and gives back every melee slot it is part of.
    /// </summary>
    private void TearDownCorpse(ICreature creature)
    {
        // The same teardown RemoveCreature does, repeated here because death has to take effect
        // immediately. RemoveCreature does eventually run for a corpse — ICorpseRemover schedules it
        // BodyRemoveTimer from now — but a creature that keeps walking, holds a melee slot and shoves
        // the crowd about for those seconds is exactly the bug. Doing it at this chokepoint rather than
        // in the script's death branch covers every death route: every kill is a hit through this
        // instance's combat service, which calls this once, whatever the creature's script is.
        // Everything below is idempotent, so the later RemoveCreature repeating it is harmless.
        //
        // Stop BEFORE Unregister, not after: Stop is what brings the corpse to rest (MoveState.Idle,
        // zero Velocity) and both implementations no-op on an unregistered creature, so the reverse
        // order would leave a corpse broadcasting MoveState.Running forever. Unregister then drops the
        // waypoint queue / crowd agent, so the corpse neither keeps walking its remaining path nor
        // lingers as an invisible obstacle that living creatures steer around and the crowd's own
        // collision resolution shoves about.
        _locomotion.Stop(creature);
        _locomotion.Unregister(creature);

        // Both directions of the slot ledger, and both are needed. ReleaseClaimant gives back the
        // slot this creature held on whatever it was attacking (the script's death branch does that
        // too, but only for a creature that had a CreatureCombatScript to run it). ReleaseTarget frees
        // the ring other creatures claimed ON this one: the script-side target-death release is gated
        // on `_target is ICharacter`, so a creature target dying is not covered there. Both are
        // idempotent, so the overlap with the script is harmless.
        _meleeSlots.ReleaseClaimant(creature.Guid);
        _meleeSlots.ReleaseTarget(creature.Guid);
    }

    /// <summary>
    /// Splits the kill's experience among the eligible (2026-09-30), then awards each share through the band.
    /// The split leaves out anyone the level gap or more above the creature, solo too.
    /// </summary>
    private void AwardExperience(ICreature creature, IReadOnlyList<ICharacter> eligible)
    {
        if (eligible.Count == 0)
        {
            return;
        }

        PartyExperienceMode mode = _parties?.PartyOf(eligible[0].Guid.Id)?.ExperienceMode ?? PartyExperienceMode.Even;
        GameConfiguration config = _world.Configuration;

        // creature.Experience is the value derived at spawn — base stats by level, scaled by the template's
        // modifiers and its rarity, or the template's authored override if it had one. Not Metadata.Experience,
        // which is only that optional override.
        foreach (ExperienceShare share in PartyExperience.Split(creature.Experience, creature.Level, eligible, mode,
                     config.PartyExperienceBonusPerExtra, config.PartyExperienceLevelGap))
        {
            AwardExperience(share.Member, share.Experience);
        }
    }

    /// <summary>
    /// Gives a character its share of a kill's experience, scaled by this map's level band, and levels it up
    /// when that reaches its level's requirement. A level with no requirement awards nothing.
    /// </summary>
    private void AwardExperience(ICharacter character, uint experience)
    {
        CharacterLevelExperience? expRequirement =
            _world.Data.CharacterLevelExperiences.FirstOrDefault(exp => exp.Level == character.Level);
        if (expRequirement is null)
        {
            _logger.LogWarning("Experience requirement for level {Level} not found", character.Level);
            return;
        }

        uint creatureExperience = ScaledExperience(character, experience);
        if (character.Experience + creatureExperience >= expRequirement.Experience)
        {
            LevelUp(character, creatureExperience, expRequirement);
        }
        else
        {
            character.Experience += creatureExperience;
        }
    }

    /// <summary>This character's share of the kill's experience, scaled by this map's level band.</summary>
    private uint ScaledExperience(ICharacter character, uint experience)
    {
        MapTemplate? mapTemplate = _world.MapTemplates.FirstOrDefault(map => map.Id == TemplateId);

        double bandScale = BandScale(
            character.Level,
            mapTemplate?.MinLevel,
            mapTemplate?.MaxLevel,
            _world.Configuration.ExperienceBandDecay);

        return (uint)Math.Round(experience * bandScale, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Raises the character one level, carrying over the experience past the requirement, and
    /// recalculates its stats at the new level.
    /// </summary>
    private void LevelUp(ICharacter character, uint creatureExperience, CharacterLevelExperience expRequirement)
    {
        ulong diff = character.Experience + creatureExperience - expRequirement.Experience;
        character.Level++;
        character.Experience = diff;
        character.RequiredExperience = _world.Data.CharacterLevelExperiences
            .FirstOrDefault(exp => exp.Level == character.Level)?.Experience ?? 0;

        // #434: the new level's stats. A living killer has health and power refilled to the new
        // maximums. A kill can land after its killer has died (a projectile in flight): a dead
        // killer gets the new maximums but keeps its share of each pool instead, so its health
        // stays at 0 and it is not revived.
        if (character is CharacterEntity entity
            && !CharacterStatsRefresh.Apply(entity, _world.Data,
                entity.IsDead ? CurrentValues.KeepShare : CurrentValues.Refill))
        {
            _logger.LogWarning("No class stats for {Class} level {Level}; {Name} keeps its old maximums",
                entity.Class, entity.Level, entity.Name);
        }

        // The party roster shows levels.
        _parties?.LevelChanged(character);
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

    private sealed class PerPlayerBroadcastState
    {
        // Capacities sized for a typical instance (32 entities visible per player).
        // List<T> grows automatically if exceeded — this avoids early reallocation.
        public List<ObjectState> AddedObjects   { get; } = new(32);
        public List<ObjectState> UpdatedObjects { get; } = new(32);
    }
}
