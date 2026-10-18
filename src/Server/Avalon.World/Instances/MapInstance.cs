using System.Diagnostics;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auras;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.Loot;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using Avalon.Network.Packets.State;
using Avalon.World.Abilities;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Auras;
using Avalon.World.Characters;
using Avalon.World.ChunkLayouts;
using Avalon.World.Combat;
using Avalon.World.Configuration;
using Avalon.World.Creatures;
using Avalon.World.Creatures.Locomotion;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Items;
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
using Avalon.World.Telemetry;
using Avalon.World.Vendors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.World.Instances;

public class MapInstance : IMapInstance, IPortalSink, IGroundLootHost, IVendorHost, IAbilityArena, ICombatOutcomes,
    IItemUseHost, IItemCastAudience, IDoodadHost, IDoodadCastAudience, IAuraHost, IPlayerCastHost, IDisposable
{
    private InstanceTickProfile _tickProfile;

    private readonly Dictionary<ObjectGuid, ICharacter> _characters = [];
    private readonly Dictionary<ObjectGuid, IWorldConnection> _connections = [];
    private readonly ICorpseRemover _corpseRemover;
    private readonly Dictionary<ObjectGuid, ICreature> _creatures = [];

    // The characters and creatures above by where they stand (#982): who can be in a client's view is looked for in the
    // cells around it. Holds exactly the dictionaries' entities: every change to either changes it too.
    private readonly SpatialGrid _members;

    // The doodads placed here, by where they stand, in the cells of the grid above. Filled as the instance is built and
    // never again: doodads do not move.
    private readonly DoodadIndex _doodads;

    // The doodads whose state changed since the last broadcast, each once (Doodad.ChangeQueued), marked dirty for it.
    private readonly List<Doodad> _changedDoodads = new(8);

    // Who may see and use a doodad, asked by each view decision of a doodad with rules (CharacterCharacterGameState).
    private readonly DoodadVisibility _doodadVisibility;

    // Doodad use: the container's use service (none in a test that builds an instance without it, which then uses no
    // doodad), the cast bars it starts here, and the spent doodads waiting to respawn.
    private readonly DoodadUseService? _doodadUses;
    private readonly DoodadCasts _doodadCasts;
    private readonly DoodadRespawns _doodadRespawns = new();

    // Off only for a test that compares the views over the index against the full scan (DecideViewsByFullScan).
    private bool _viewsOverIndex = true;

    // Which creatures run their script on a tick (#979): the active ones, and the waiting ones when due or woken. Off
    // only for a test that compares it against every creature on every tick (UpdateEveryCreatureEveryTick).
    private readonly CreatureScheduler _scheduler;
    private bool _scheduleCreatures = true;

    // The creature clock (#979): the sum of the deltas of the ticks on which the creature scripts ran.
    private TimeSpan _creatureClock;
    private readonly ILogger<MapInstance> _logger;
    private readonly IMapNavigator _navigator;
    private readonly ICreatureLocomotion _locomotion;
    private readonly float _creatureAgentRadius;
    private readonly bool _crowdIncludesPlayers;
    private readonly InterestRange _interest;
    private readonly MeleeSlots _meleeSlots;
    private readonly IAbilityCastSystem _abilityCastSystem;
    private readonly PendingHits _pendingHits;
    private readonly ItemUseCasts _itemUses;
    private readonly EncounterRegistry _encounterRegistry;
    private readonly CombatService _combatService;
    private readonly AuraSystem _auras;

    // The aura pass, an ability's aura application and ending a unit's auras on its death, each logged at most once per
    // ThrottledErrorLog.Interval when it throws, so one that throws every tick, cast or death cannot flood the log.
    private readonly ThrottledErrorLog _auraPassFailures;
    private readonly ThrottledErrorLog _auraApplyFailures;
    private readonly ThrottledErrorLog _auraDeathFailures;

    // A recipient's state broadcast that threw (#981), logged at most once per ThrottledErrorLog.Interval.
    private readonly ThrottledErrorLog _broadcastFailures;

    // Set by a tick that found nobody here, so the aura pass did not run: the next tick with players skips the ticks
    // owed meanwhile rather than paying them all at once.
    private bool _aurasPaused;
    private readonly UnitHitQuery _hits;
    private readonly ThreatBroadcastService _threatBroadcast;
    private readonly IWorld _world;

    // The state broadcast's clock (#975): how far this instance is into its broadcast interval, in TimeSpan ticks,
    // always below BroadcastSchedule.Interval. It starts from the phase the registry gives the instance as it publishes
    // it and runs on every tick, empty ones included, so the instance keeps its slot on the world's schedule.
    private long _broadcastClock;

    // Whether this tick broadcasts state, decided once at the top of the tick: the dirty snapshot, and with it the state
    // updates, and the tick profile all read it.
    private bool _broadcastTick;

    // Whether every recipient's view is owed a decision (#981): set by a broadcast tick, cleared once every view has been
    // decided, so a tick that throws before then has them decided on the next tick rather than a period later.
    private bool _viewsOwed;

    // The characters that left since the last broadcast pass (#981), whose removal is reported to every client that held
    // them on the next pass rather than at the views' next decision (CharacterCharacterGameState.CharacterLeft).
    private readonly List<ObjectGuid> _departedCharacters = new(4);
    private readonly Dictionary<ObjectGuid, GameEntityFields> _frameDirtyFields = new(256);

    /// <summary>
    /// The messages every player's state broadcast describes its view with (#640), reset for each player:
    /// each is encoded as soon as it is described, so the next player can have the messages back.
    /// </summary>
    private readonly ObjectStatePool _statePool = new();

    /// <summary>The aura lists' and updates' entries, reused from one packet to the next (#980).</summary>
    private readonly AuraWire _auraWire;

    /// <summary>
    /// What the replication packets (world state and auras) are encoded with: <see cref="PacketEncoder.Shared" />, unless
    /// the container holds one (a test that counts the payloads the broadcast has out, #980).
    /// </summary>
    private readonly PacketEncoder _replicationEncoder;

    /// <summary>Each changed unit's aura update, encoded once per broadcast pass for every client that hears it (#980).</summary>
    private readonly SharedAuraUpdates _auraUpdates;

    /// <summary>Each player's state adds and updates, put together from each entity's entry, encoded once per broadcast (#980).</summary>
    private readonly StatePacketAssembler _stateAdds = StatePacketAssembler.ForAdds();
    private readonly StatePacketAssembler _stateUpdates = StatePacketAssembler.ForUpdates();

    // Whether this tick's broadcast puts its adds and updates together from shared entries: only with at least
    // StatePacketAssembler.MinRecipientsToShare characters here; with fewer, each list is encoded whole (#980).
    private bool _shareStateEntries;

    /// <summary>
    /// The world's tick count (#1066), from the container: every add and update names the tick that built it, and every
    /// cast start the tick it was taken on (#1082). Null in an instance built without one (tests), whose packets carry
    /// tick 0.
    /// </summary>
    private readonly WorldTicks? _worldTicks;

    /// <summary>
    /// The members' position histories (#1082) not in use: one is rented as a unit joins and returned as it leaves, so a
    /// ring is allocated once per pooled history and a unit coming and going allocates nothing once the pool has grown to
    /// the most members this instance has held. Tick thread only, as membership is.
    /// </summary>
    private readonly Stack<PositionHistory> _histories = new();

    // The histories this instance has made. The pool's room is grown to it as each is made, so a return (a corpse
    // removed on the tick) never grows the stack.
    private int _historiesMade;

    // The tick this tick's adds and updates carry, read once as the broadcast begins.
    private long _stateTick;

    // One recipient's described adds or updates, encoded whole when nothing is shared, reused for every packet (#980).
    private readonly List<ObjectState> _described = new(32);
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

    // Whether a character is in a party leave countdown, asked once per member of every kill: made once, where a
    // delegate made from the party service's method at each kill was an object per kill (#890).
    private readonly Func<uint, bool> _inCountdown;

    // Whether a character can still gain experience over the reference data as it is now, asked of every member that
    // shares a kill: made once, like the one above.
    private readonly Func<ICharacter, bool> _canGainExperience;

    // One kill's lists, emptied when it is done (#890). A kill reached while another is being handled (nothing does
    // that today: a hook run by a kill would have to kill another creature here) takes lists of its own.
    private readonly KillBuffers _killBuffers = new();
    private bool _killInProgress;

    // This map's template, for the level band of a kill's experience, with the world's list it was found in. Map
    // templates are read once, at startup, and never reloaded; the world holds them as one list it would replace
    // whole, so the template is looked for again only when the world answers with another list (#883).
    private IReadOnlyList<MapTemplate>? _bandTemplatesFrom;
    private MapTemplate? _bandTemplate;

    /// <summary>
    /// Vendor stock (#432): one state per vendor creature that has had a shop open here. It lives as
    /// long as the instance, so for the persistent town a restart is what resets it.
    /// </summary>
    private readonly VendorStocks _vendors = new();

    private readonly TimeProvider _time;

    // Item use: each summoned creature and when it leaves if still alive, on the instance's clock. Tick thread.
    private readonly Dictionary<ObjectGuid, (ICreature Creature, DateTimeOffset LeavesAt)> _summons = [];
    private readonly List<ICreature> _summonsDone = [];

    // Creature health scaling (2026-09-30), party instances only: set when someone enters or leaves, applied once at
    // the start of the next Update, so several changes in one tick make one rescale and one message.
    private double _healthFactor = 1d;
    private bool _healthFactorOwed;
    private int _presenceChanges;
    private string? _presenceChange;

    /// <summary>The seeded combat formula, for an instance whose world has no reference data loaded (tests).</summary>
    private static readonly Avalon.Domain.World.CombatFormula s_seededFormula = Avalon.Database.World.Seeding.CombatSeed.Formula();
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
        _members = SpatialGrid.For(layout);
        _doodads = DoodadIndex.For(layout);
        _scheduler = new CreatureScheduler(_creatures, this, _members);
        _world = world;
        InstanceId = Guid.NewGuid();
        TemplateId = templateId;
        MapType = mapType;
        OwnerCharacterId = ownerCharacterId;
        OwnerPartyId = ownerPartyId;
        AllowedCharacters = ownerCharacterId.HasValue ? new[] { ownerCharacterId.Value } : Array.Empty<uint>();
        Layout = layout;
        EntrySpawnWorldPos = layout.EntrySpawnWorldPos;

        // The layout's packet entries (#883), here rather than at the first arrival: an instance is built off the
        // tick, and the first character to arrive should cost the tick what every other one does. The chunk library
        // that names the chunks is required, like the combat settings and the script manager below: without it the
        // build fails here, loudly, rather than making an instance whose arrivals are told of a map and sent no
        // layout to build it from. A chunk the library does not hold fails the build the same way, as it already
        // failed the navmesh bake before it.
        LayoutEntries = new MapLayoutEntries(InstanceId, layout, serviceProvider.GetRequiredService<IChunkLibrary>());
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
        _combatService = new CombatService(combatConfig, _encounterRegistry, this, _pvp, outcomes: this, time: _time,
            furyFromDamageTaken: world.Configuration.FuryFromDamageTaken,
            random: serviceProvider.GetService<ICombatRandom>(),
            formula: () => world.Data?.Combat?.Formula ?? s_seededFormula);
        _threatBroadcast = new ThreatBroadcastService(combatConfig, _time);

        // Auras: one system per instance over its own units, ticked right after the ability cast system on this
        // instance's clock. The catalog and the reference data are read through the world each time, so a reload
        // reaches the next application. A world built without reference data (tests) has no aura at all.
        _auras = new AuraSystem(_combatService, _characters, _creatures,
            () => world.Data?.Auras ?? AuraCatalog.Empty, () => world.Data, _time, world.Configuration.MaxAurasPerUnit,
            loggerFactory.CreateLogger<AuraSystem>(), serviceProvider.GetService<AuraScripts>(),
            guid => _connections.TryGetValue(guid, out IWorldConnection? connection) ? connection : null);
        _auraPassFailures = new ThrottledErrorLog(_logger, _time, $"The aura pass of instance {InstanceId}");
        _auraApplyFailures = new ThrottledErrorLog(_logger, _time, $"An ability's aura in instance {InstanceId}");
        _auraDeathFailures = new ThrottledErrorLog(_logger, _time, $"Ending the auras of a unit on its death in instance {InstanceId}");
        _broadcastFailures = new ThrottledErrorLog(_logger, _time, $"A client's state broadcast in instance {InstanceId}");
        _replicationEncoder = serviceProvider.GetService<PacketEncoder>() ?? PacketEncoder.Shared;
        _worldTicks = serviceProvider.GetService<WorldTicks>();
        _auraWire = new AuraWire();
        _auraUpdates = new SharedAuraUpdates(_auraWire, _replicationEncoder);

        // Shape scripts ask this for the living units their shape overlaps (#164), found in the cells of the grid around
        // the shape (#982), rewound against the world's tick count for a player's instant cast (#1082).
        _hits = new UnitHitQuery(_characters, _creatures, _members, _worldTicks);

        // The cast system gets `this` as the IAbilityArena every ability script is built with
        // (#164): scripts route damage through CombatService.ApplyDamage. CombatService must be
        // assigned BEFORE this so any first-tick cast resolves through a non-null service.
        var castSystem = new InstanceAbilityCastSystem(loggerFactory, serviceProvider,
            serviceProvider.GetRequiredService<IScriptManager>(), this);
        _abilityCastSystem = castSystem;
        // The dodge wait's hits (#1082): added here as a telegraph fires, resolved and dropped by the cast system.
        _pendingHits = castSystem.PendingHits;

        // Item cast bars, numbered with the cast system's own ids.
        _itemUses = new ItemUseCasts(this, _abilityCastSystem.TakeCastId, loggerFactory.CreateLogger<ItemUseCasts>());

        // Doodad cast bars, numbered the same way, on the container's clock, answering to the use service that starts
        // them. Without one nothing starts a doodad cast, so the stand-in owner is never asked.
        _doodadUses = serviceProvider.GetService<DoodadUseService>();
        _doodadCasts = new DoodadCasts(this, this, (IDoodadCastOwner?)_doodadUses ?? NoDoodadCastOwner.Instance,
            _abilityCastSystem.TakeCastId, _time, loggerFactory.CreateLogger<DoodadCasts>());

        // Optional so an instance built without one (tests) simply has no periodic save.
        _saveScheduler = serviceProvider.GetService<ICharacterSaveScheduler>();

        // Optional for the same reason: an instance built without them (tests) drops nothing.
        // Production registers both; WorldHostGraphShould proves it.
        _lootRoller = serviceProvider.GetService<ILootRoller>();
        _lootAllocator = serviceProvider.GetService<ILootAllocator>();
        _questService = serviceProvider.GetService<QuestService>();

        // Optional too: an instance built without it (tests) shares nothing, so every kill is its killer's alone.
        _parties = serviceProvider.GetService<PartyService>();
        _inCountdown = _parties is null ? static _ => false : _parties.InCountdown;
        _canGainExperience = member => ExperienceAward.CanGainExperience(member, _world.Data);

        // Vendor quest gates (#432). Falls back, so an instance built without it (tests) still
        // ticks; production registers it.
        _quests = serviceProvider.GetService<IQuestProgress>() ?? NoQuestProgress.Instance;

        // Doodad gates: the container's one set of rules, and a set of its own for an instance built without (tests).
        _doodadVisibility = serviceProvider.GetService<DoodadVisibility>() ?? new DoodadVisibility();
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
        _summons.Clear();
        _departedCharacters.Clear();
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

    /// <summary>
    /// Whether a character has ever been added. An instance nobody has entered yet (a build its requester has still to
    /// reach, or a disbanded party's) is not abandoned: <see cref="InstanceRegistry" /> gives it the fixed
    /// <see cref="InstanceRegistry.UnenteredInstanceLifetime" /> rather than <c>Game:AbandonedInstanceLifetimeMinutes</c>.
    /// Tick thread, as is every membership change.
    /// </summary>
    internal bool HasBeenEntered { get; private set; }
    public int Seed { get; }
    public string ConfigVersion => Layout.ConfigVersion ?? string.Empty;

    /// <summary>
    /// The chunk layout this instance was built from. Every instance has one (#912): it is what a client is sent
    /// behind a successful arrival. World-side, not on IMapInstance.
    /// </summary>
    public ChunkLayout Layout { get; }

    /// <summary>
    /// <see cref="Layout" /> as the <c>SMSG_CHUNK_LAYOUT</c> of every character arriving here carries it (#883): built
    /// with the instance, so off the tick like the rest of it, and never changed. Every instance has them, as it has
    /// a layout (#912): a client completes a transition on the layout it is sent behind it, so an instance that could
    /// not send one must not exist. World-side, not on IMapInstance.
    /// </summary>
    internal MapLayoutEntries LayoutEntries { get; }

    public Vector3 EntrySpawnWorldPos { get; }
    public IReadOnlyList<PortalInstance> Portals => _portals;

    /// <summary>The doodads placed here. World-side, not on IMapInstance. Read-only once the instance is published.</summary>
    public DoodadIndex Doodads => _doodads;

    public IReadOnlyDictionary<ObjectGuid, ICharacter> Characters => _characters;

    /// <summary>
    /// Every connection whose character is in this instance. World-side, not on IMapInstance: the modding API
    /// must not reach other players' connections. Tick thread only. The dictionary's own view, read-only, so a
    /// foreach over it takes the struct enumerator rather than boxing one (#973).
    /// </summary>
    public Dictionary<ObjectGuid, IWorldConnection>.ValueCollection Connections => _connections.Values;
    public IReadOnlyDictionary<ObjectGuid, ICreature> Creatures => _creatures;
    public ICombatService CombatService => _combatService;

    /// <summary>The auras of this instance's units (auras). World-side, not on IMapInstance.</summary>
    public AuraSystem Auras => _auras;

    /// <summary>The item cast bars here. World-side, not on IMapInstance.</summary>
    public ItemUseCasts ItemUses => _itemUses;

    /// <summary>The doodad cast bars here. World-side, not on IMapInstance.</summary>
    public DoodadCasts DoodadCasts => _doodadCasts;

    DoodadRespawns IDoodadHost.Respawns => _doodadRespawns;

    TimeProvider IDoodadHost.Clock => _time;

    void IDoodadHost.DoodadChanged(Doodad doodad) => DoodadChanged(doodad);

    uint IItemUseHost.RestoreHealth(IUnit healer, IUnit target, uint amount) =>
        _combatService.RestoreHealth(healer, target, amount);

    bool IItemUseHost.CastForItem(IUnit caster, AbilityAim aim, IAbility ability, bool free) =>
        ability.Metadata.CastTime > 0
            ? _abilityCastSystem.QueueAbility(caster, aim, ability, free)
            : _abilityCastSystem.RunInstant(caster, aim, ability, free);

    void IItemUseHost.DespawnAfter(ICreature creature, TimeSpan lifetime) =>
        _summons[creature.Guid] = (creature, _time.GetUtcNow() + lifetime);

    public IHitQuery Hits => _hits;
    public ICreatureLocomotion Locomotion => _locomotion;
    public IMeleeSlots MeleeSlots => _meleeSlots;
    public TimeSpan CreatureClock => _creatureClock;

    /// <summary>
    /// Runs every creature's script on every tick, as before #979, rather than only the active and the due: the reference
    /// the scheduling is compared against. For tests only, before the instance's first tick; World-side and internal, so
    /// reached by name.
    /// </summary>
    internal void UpdateEveryCreatureEveryTick()
    {
        _scheduleCreatures = false;
        foreach (ICreature creature in _creatures.Values)
            _scheduler.Left(creature);
    }

    /// <summary>
    /// Runs every due creature's search, as before #982, rather than skipping the ones no character can be within range
    /// of: the reference the filter is compared against. For tests only; World-side and internal, so reached by name.
    /// </summary>
    internal void SearchEveryDueCreature() => _scheduler.SearchEveryDueCreature();

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
        // Encoded once, for the first connection here, and shared by every one (#875).
        OutboundPacket notice = default;
        foreach (IWorldConnection connection in _connections.Values)
        {
            if (notice.Payload is null)
            {
                notice = SChatMessagePacket.System(text, now, PacketEncoder.Shared);
            }

            connection.Send(notice.Share());
        }

        notice.Release();
    }

    public bool IsExpired(TimeSpan expiry) =>
        LastEmptyAt.HasValue && (_time.GetUtcNow().UtcDateTime - LastEmptyAt.Value) >= expiry;

    public bool CanAcceptPlayer(ushort maxPlayers) => _characters.Count < maxPlayers;

    public void AddPortal(PortalInstance portal) => _portals.Add(portal);

    public IMapNavigator GetNavigatorForPosition(Vector3 position) => _navigator;

    /// <summary>
    /// Chooses the locomotion implementation per <see cref="GameConfiguration.CreatureLocomotion" />.
    /// <see cref="CrowdLocomotion" /> needs a non-null baked <see cref="DotRecast.Detour.DtNavMesh" />, but the
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
                connection.Send(SInstanceStateRemovePacket.Create(forgotten, _replicationEncoder));

            // #526: every instance move comes through here (map entry, respawn at a town, a portal), and
            // each one starts with no Fury. Mana and Energy are kept.
            arriving.ResetFury();
        }

        // An instance that stood empty skips the ticks its units were owed meanwhile now, before the arrival is a member:
        // the arriving character's auras ran elsewhere, or stood still while it loaded, and owe nothing to this stretch.
        if (_aurasPaused)
            ResumeAuras();

        ICharacter joining = connection.Character!;
        if (_characters.TryGetValue(joining.Guid, out ICharacter? replaced) && !ReferenceEquals(replaced, joining))
        {
            _members.Remove(replaced);
            if (replaced is CharacterEntity replacedEntity)
                replacedEntity.History = ReturnHistory(replacedEntity.History);
        }
        _characters[joining.Guid] = joining;
        if (joining is CharacterEntity joiningEntity)
            joiningEntity.History = RentHistory(joiningEntity.History);
        _members.Add(joining, SpatialKind.Character);
        _auras.Roster.Join(joining);
        _connections[joining.Guid] = connection;
        _lootSnapshotOwed.Add(joining.Guid);
        _pvpStateOwed.Add(joining.Guid);
        LastEmptyAt = null;
        HasBeenEntered = true;

        // Auras restored at select stood still while the client loaded, and a moved character's while it was between
        // instances (World.TransferPlayer); their time starts again now, here.
        if (connection.Character is CharacterEntity entered)
            entered.Auras.ResumeHeld(entered.Clock.GetUtcNow());

        NotePresenceChange(joining, "entered");
    }

    /// <summary>
    /// Ends everything the character has casting here as it leaves: its ability casts, its running ability scripts, its
    /// item cast bar and its doodad cast bar. Each step is contained, so a failure cannot keep the character a member.
    /// </summary>
    private void EndCastsOf(ICharacter character)
    {
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

        // An item's cast bar ends here too, out loud, while the character is still a member, and its use is answered
        // Interrupted. Contained, like the casts above.
        try
        {
            _itemUses.Interrupt(guid);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Interrupting the item use of {CharacterGuid} as it left instance {InstanceId} failed",
                guid, InstanceId);
        }

        // And its doodad's cast bar, out loud, while it is still a member, so everyone here sees it end.
        try
        {
            _doodadCasts.Interrupt(guid);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Interrupting the doodad use of {CharacterGuid} as it left instance {InstanceId} failed",
                guid, InstanceId);
        }
    }

    public void RemoveCharacter(IWorldConnection connection)
    {
        ICharacter character = connection.Character!;
        ObjectGuid guid = character.Guid;

        EndCastsOf(character);

        // Membership next. These cannot throw, and once they are gone the tick no longer updates,
        // broadcasts or periodically saves the character, whatever the hooks below do. A disconnect
        // hook that threw ahead of them used to leave a despawned character live in the instance.
        if (_characters.Remove(guid, out ICharacter? removed))
        {
            _members.Remove(removed);
            if (removed is CharacterEntity removedEntity)
                removedEntity.History = ReturnHistory(removedEntity.History);
        }
        _auras.Roster.Leave(character);
        _connections.Remove(guid);
        _lootSnapshotOwed.Remove(guid);
        _pvpStateOwed.Remove(guid);
        _departedCharacters.Add(guid);
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
                // A World-side creature is woken first, so a waiting script acts on it (#979).
                if (creature is Creature scheduled)
                    scheduled.OnCharacterLeft(character);
                else
                    creature.Script?.OnCharacterLeft(character);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "The script of creature {CreatureGuid} failed on {CharacterGuid} leaving instance {InstanceId}",
                    creature.Guid, character.Guid, InstanceId);
            }
        }
    }

    /// <summary>
    /// Places a doodad here. Build time only, off the tick, before the instance is published: the index is never written
    /// once a tick can read it, and throws once <see cref="MarkPublished" /> sealed it. Its guid must be a doodad's, which is
    /// what routes its description.
    /// </summary>
    public void AddDoodad(Doodad doodad)
    {
        if (doodad.Guid.Type != ObjectType.Doodad)
            throw new ArgumentException($"A doodad's guid must be of type Doodad, not {doodad.Guid.Type}.", nameof(doodad));

        _doodads.Add(doodad);
    }

    /// <summary>
    /// The registry publishes the instance (on the tick, before its first tick): from here its doodads are read by the
    /// tick, so <see cref="AddDoodad" /> is refused.
    /// </summary>
    internal void MarkPublished() => _doodads.Seal();

    /// <summary>
    /// The quests its world holds now (a doodad spawn's gate is checked against them as the instance is built); empty
    /// before the first load.
    /// </summary>
    internal QuestCatalog Quests => _world.Data?.Quests ?? QuestCatalog.Empty;

    /// <summary>
    /// Tick thread only: <paramref name="doodad" />'s state changed, so every client that sees it is owed an update of its
    /// state and of whether it can use it, on the next broadcast. Queued once however often it changes before then;
    /// nothing walks the doodads that did not change.
    /// </summary>
    internal void DoodadChanged(Doodad doodad)
    {
        if (doodad.ChangeQueued)
            return;

        doodad.ChangeQueued = true;
        _changedDoodads.Add(doodad);
    }

    public void AddCreature(ICreature creature)
    {
        if (_creatures.TryGetValue(creature.Guid, out ICreature? replaced) && !ReferenceEquals(replaced, creature))
        {
            _members.Remove(replaced);
            if (replaced is Creature replacedCreature)
                replacedCreature.History = ReturnHistory(replacedCreature.History);
        }
        _creatures[creature.Guid] = creature;
        if (creature is Creature joining)
            joining.History = RentHistory(joining.History);
        _members.Add(creature, SpatialKind.Creature);
        if (_scheduleCreatures)
            _scheduler.Joined(creature);
        _auras.Roster.Join(creature);
        _locomotion.Register(creature, _creatureAgentRadius);

        // A creature spawned later in a party instance starts at the health the players here make.
        if (_healthFactor != 1d && creature is Creature scaled)
            scaled.Rescale(_healthFactor);
    }

    /// <summary>
    /// Step 1b of <see cref="Update" /> (item use): forgets a summon that is gone or dead (a dead one is the corpse
    /// removal's), and removes a living one whose time has come, dropped from its encounter first so no threat
    /// outlives it. Allocates nothing while there is no summon.
    /// </summary>
    private void DespawnExpiredSummons()
    {
        if (_summons.Count == 0)
            return;

        DateTimeOffset now = _time.GetUtcNow();
        _summonsDone.Clear();
        foreach ((ObjectGuid guid, (ICreature creature, DateTimeOffset leavesAt)) in _summons)
        {
            if (!_creatures.ContainsKey(guid) || creature.CurrentHealth == 0 || now >= leavesAt)
                _summonsDone.Add(creature);
        }

        foreach (ICreature creature in _summonsDone)
        {
            _summons.Remove(creature.Guid);

            // Each summon on its own: one that throws costs neither the tick nor the other summons their removal.
            try
            {
                if (creature.CurrentHealth == 0 || !_creatures.ContainsKey(creature.Guid))
                    continue;

                ((IHostileEncounterExit)_combatService).DropHostileFromEncounter(creature);
                RemoveCreature(creature);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Removing the expired summon {Creature} ({Name}) from instance {InstanceId} threw",
                    creature.Guid, creature.Name, InstanceId);
            }
        }
    }

    public void RemoveCreature(ICreature creature)
    {
        if (_creatures.Remove(creature.Guid, out ICreature? removed))
        {
            _members.Remove(removed);
            if (removed is Creature removedCreature)
                removedCreature.History = ReturnHistory(removedCreature.History);
        }
        _scheduler.Left(creature);
        _auras.Roster.Leave(creature);

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
    /// A player's instant cast, judged by that player's screen (#1082): its hits are rewound from this tick to the one the
    /// client named, within the cap, and a projectile's sweep by that less the caster's one-way delay, never below 0. The
    /// round trip is read only when there is a rewind to shorten. No world tick count (tests), no rewind.
    /// </summary>
    bool IPlayerCastHost.RunInstantAbility(ICharacter caster, AbilityAim aim, IAbility ability, long viewTick)
    {
        CastView view = default;
        if (_worldTicks is not null)
        {
            int rewind = LagCompensation.RewindTicks(viewTick, _worldTicks.Completed);
            if (rewind > 0)
            {
                int oneWay = LagCompensation.OneWayTicks(RoundTripOf(caster.Guid));
                view = new CastView(rewind, Math.Max(0, rewind - oneWay));
            }
        }

        return _abilityCastSystem.RunInstant(caster, aim, ability, view: view);
    }

    /// <summary>
    /// The last round trip of <paramref name="guid" />'s connection, in UTC ticks (<c>IWorldConnection.RoundTripTime</c>),
    /// for lag compensation (#1082); 0 for a unit with no connection here, or a round trip not above 0 (none measured
    /// yet). Advisory, as the round trip is: it may only shorten a rewind, never lengthen one past the cap.
    /// </summary>
    private long RoundTripOf(ObjectGuid guid) =>
        _connections.TryGetValue(guid, out IWorldConnection? connection) && connection.RoundTripTime > 0
            ? connection.RoundTripTime
            : 0;

    int IAbilityArena.HitRewindTicks
    {
        get => _hits.RewindTicks;
        set => _hits.RewindTicks = value;
    }

    IUnit? IAbilityArena.HitRewindExempt
    {
        get => _hits.RewindExempt;
        set => _hits.RewindExempt = value;
    }

    // The footprint of the telegraph the cast system is firing now (#1082); see IAbilityArena.DodgeFootprint.
    private AbilityFootprint? _dodgeFootprint;

    AbilityFootprint? IAbilityArena.DodgeFootprint
    {
        get => _dodgeFootprint;
        set => _dodgeFootprint = value;
    }

    /// <summary>
    /// A telegraph's hit on a player, judged by that player's screen (#1082): held for the player's one-way delay, read from
    /// their connection's round trip and capped, then landed only if they are still inside. No wait (no round trip measured,
    /// no world tick count in tests, or a delay under half a tick), no hold: the hit lands now, as before.
    /// </summary>
    bool IAbilityArena.TryDeferHit(IUnit caster, IAbility ability, IUnit target)
    {
        if (_dodgeFootprint is not { } footprint || target is not ICharacter character || _worldTicks is null)
            return false;

        int wait = Math.Min(LagCompensation.OneWayTicks(RoundTripOf(character.Guid)), LagCompensation.MaxDodgeWaitTicks);
        if (wait <= 0)
            return false;

        _pendingHits.Add(new PendingHit(caster, ability, character, footprint, _worldTicks.Completed + wait));
        return true;
    }

    /// <summary>
    /// Sends a hit to everyone who hears it (#532). A creature script's broadcast, made during the hit's
    /// OnHit, is marked with how the hit went (#506); any other is None.
    /// </summary>
    public void BroadcastUnitHit(IUnit attacker, IUnit target, uint currentHealth, uint damage) =>
        BroadcastUnitHit(attacker, target, currentHealth, damage, _hitInFlight);

    private void BroadcastUnitHit(IUnit attacker, IUnit target, uint currentHealth, uint damage, HitResult result)
    {
        // Encoded once, for the first who hears it, and shared by everyone who does (#875).
        OutboundPacket hit = default;
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, attacker.Guid, target.Guid, attacker.Position, target.Position))
            {
                continue;
            }

            if (hit.Payload is null)
            {
                hit = SUnitDamagePacket.Create(attacker.Guid, target.Guid.RawValue,
                    currentHealth, damage, PacketEncoder.Shared, result);
            }

            connection.Send(hit.Share());
        }

        hit.Release();
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
        // Encoded once, for the first who hears it, and shared by everyone who does (#875).
        OutboundPacket start = default;
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, caster.Guid, null, caster.Position, centre))
            {
                continue;
            }

            if (start.Payload is null)
            {
                // #627: the time the cast system just set, haste included, so every cast bar ends when the cast does.
                // #1082: the tick the cast was taken on, so a client ends it at that tick's server time plus CastTime.
                start = SUnitStartCastPacket.Create(caster.Guid, ability.CastTimeTimer,
                    ability.AbilityId.Value, castId, dto, _worldTicks?.Completed ?? 0, PacketEncoder.Shared);
            }

            connection.Send(start.Share());
        }

        start.Release();
    }

    // The cast whose script the cast system is firing now (#648); see IAbilityArena.CastInFlight.
    private uint _castInFlight;

    uint IAbilityArena.CastInFlight { set => _castInFlight = value; }

    public void BroadcastAbilityFired(IUnit caster, IAbility ability, AbilityFootprint footprint)
    {
        AbilityFootprintDto dto = footprint.ToDto();
        // Encoded once, for the first who hears it, and shared by everyone who does (#875).
        OutboundPacket fired = default;
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, caster.Guid, null, footprint.Origin, footprint.Centre))
            {
                continue;
            }

            if (fired.Payload is null)
            {
                fired = SAbilityFiredPacket.Create(caster.Guid.RawValue, ability.AbilityId.Value, _castInFlight,
                    dto, PacketEncoder.Shared);
            }

            connection.Send(fired.Share());
        }

        fired.Release();
    }

    public void BroadcastUnitDeath(IUnit unit, IUnit? killer)
    {
        // Encoded once, for the first who hears it, and shared by everyone who does (#875).
        OutboundPacket death = default;
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, unit.Guid, killer?.Guid, unit.Position, null))
            {
                continue;
            }

            if (death.Payload is null)
            {
                death = SUnitDeathPacket.Create(unit.Guid, killer?.Guid,
                    PacketEncoder.Shared);
            }

            connection.Send(death.Share());
        }

        death.Release();
    }

    public void BroadcastUnitRevive(IUnit unit, Vector3 position, uint health)
    {
        // Encoded once, for the first who hears it, and shared by everyone who does (#875).
        OutboundPacket revive = default;
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, unit.Guid, null, position, null))
            {
                continue;
            }

            if (revive.Payload is null)
            {
                revive = SUnitRevivePacket.Create(unit.Guid, position, health,
                    PacketEncoder.Shared);
            }

            connection.Send(revive.Share());
        }

        revive.Release();
    }

    /// <summary>
    /// Whether the connection of character <paramref name="guid" /> receives a one-shot effect broadcast
    /// (#532): involved in it, or within Game:InterestRadius of one of its points. A connection
    /// with no character is near nothing.
    /// </summary>
    private bool Hears(ObjectGuid guid, IWorldConnection connection, ObjectGuid involved, ObjectGuid? alsoInvolved,
        Vector3 point, Vector3? alsoPoint) =>
        EffectAudience.Receives(guid, connection.Character?.Position ?? s_unplaced, _interest.Radius,
            involved, alsoInvolved, point, alsoPoint);

    /// <summary>The position of a connection with no character: not finite, so never near an effect.</summary>
    private static readonly Vector3 s_unplaced = new(float.NaN, float.NaN, float.NaN);

    /// <summary>The guid of nobody (0), for a periodic tick whose caster is gone. Never written: only its raw value is read.</summary>
    private static readonly ObjectGuid s_nobody = new();

    public GroundLootStore Drops => _groundLoot;

    public VendorStocks Vendors => _vendors;

    public void BroadcastLootDespawned(IReadOnlyList<ObjectGuid> lootGuids)
    {
        // Encoded once, for the first connection here, and shared by every one (#875).
        OutboundPacket despawned = default;
        foreach ((ObjectGuid _, IWorldConnection connection) in _connections)
        {
            if (despawned.Payload is null)
            {
                despawned = SLootDespawnedPacket.Create(lootGuids, PacketEncoder.Shared);
            }

            connection.Send(despawned.Share());
        }

        despawned.Release();
    }

    /// <summary>
    /// Rolls, allocates and places a dying creature's drops and tells everyone here. Tick thread: CreatureKilled is
    /// called by this instance's combat service inside combat and ability processing. Reads the Loot, Items and
    /// Quests areas as they are now, so a reload applies to the next kill. Each table drop is allocated on its own,
    /// among the characters that share the kill (2026-09-30); each quest drop (#433) belongs to the member it was
    /// rolled for, for good. All of them are placed in one ring. A kill nobody is credited with
    /// (<paramref name="credited" /> false: an aura's tick whose caster is gone) drops every table drop free for all at
    /// once, whatever instance this is. It allocates the drops it leaves on the ground, and the list the roller hands
    /// back; its own lists, the quest drops among them (#883), and loot messages are the kill's
    /// <paramref name="buffers" /> (#890).
    /// </summary>
    private void DropLoot(ICreature creature, IReadOnlyList<ICharacter> eligible, bool credited, KillBuffers buffers)
    {
        List<(RolledDrop Drop, uint Owner)> questDrops = buffers.QuestDrops;
        RollQuestDrops(creature, eligible, questDrops);
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

            List<RolledDrop> rolled = buffers.Rolled;
            Debug.Assert(rolled.Count == 0, "The list of rolled drops still holds an earlier kill's");
            rolled.AddRange(table);
            for (int i = 0; i < questDrops.Count; i++)
            {
                rolled.Add(questDrops[i].Drop);
            }

            PlaceAndAnnounce(creature.Position, rolled,
                new KillAllocations(_lootAllocator, OwnerCharacterId, OwnerPartyId, eligible, credited, table.Count, questDrops),
                buffers);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not drop loot for creature {CreatureGuid} (template {TemplateId}); the kill still counts",
                creature.Guid, creature.Metadata.Id.Value);
        }
    }

    /// <summary>
    /// A doodad's drops, on a kill's rules: its template's table and copper range rolled now, against the Loot and Items
    /// areas as they are, each drop allocated on its own among the user and the members of its party in this instance (a
    /// party member anywhere here, dead or alive; a character leaves its party, and so this, at the start of a leave
    /// countdown; a user in one makes nobody eligible, as a killer in one does), placed on the ring around the doodad and
    /// told to everyone here in one shared packet. Its lists are the instance's <see cref="KillBuffers" />, as a kill's
    /// are. Contained, as a kill's drop is: a failure is logged, and what was stored before it stays (a placement that
    /// throws stores nothing). Tick thread.
    /// </summary>
    int IDoodadHost.DropLootAt(Doodad doodad, CharacterEntity user)
    {
        if (_lootRoller is null || _lootAllocator is null)
            return 0;

        int before = _groundLoot.Count;
        bool nested = _killInProgress;
        KillBuffers buffers = nested ? new KillBuffers() : _killBuffers;
        _killInProgress = true;
        try
        {
            // As for a kill: a user in a party leave countdown makes nobody eligible, so a party instance's drops are free
            // for all at once rather than reserved for one who has left the party.
            List<ICharacter> eligible = buffers.Eligible;
            if (!_inCountdown(user.Guid.Id))
                eligible.Add(user);

            if (eligible.Count > 0 && user.PartyId is { } party)
            {
                // Through the dictionary's own enumerator: a use allocates no boxed one.
                foreach (ICharacter character in _characters.Values)
                {
                    if (!ReferenceEquals(character, user) && character is CharacterEntity { PartyId: { } theirs }
                        && theirs.Equals(party))
                    {
                        eligible.Add(character);
                    }
                }
            }

            DoodadTemplateView template = doodad.Template;
            IReadOnlyList<RolledDrop> table = _lootRoller.Roll(template.LootTableId, template.MinGold, template.MaxGold,
                _world.Data.Loot, _world.Data.ItemTemplates, out bool tableMissing);
            if (tableMissing)
            {
                _logger.LogWarning(
                    "Doodad template {DoodadTemplateId} names loot table {TableId}, which is missing or was refused; it drops only gold",
                    template.Id.Value, template.LootTableId!.Value);
            }

            if (table.Count == 0)
                return 0;

            List<RolledDrop> rolled = buffers.Rolled;
            Debug.Assert(rolled.Count == 0, "The list of rolled drops still holds an earlier kill's");
            rolled.AddRange(table);
            PlaceAndAnnounce(doodad.Position, rolled,
                new KillAllocations(_lootAllocator, OwnerCharacterId, OwnerPartyId, eligible, credited: true, table.Count,
                    buffers.QuestDrops),
                buffers);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not drop loot for doodad {DoodadGuid} (template {TemplateId}) used by {CharacterGuid}",
                doodad.Guid, doodad.Template.Id.Value, user.Guid);
        }
        finally
        {
            buffers.Clear();
            _killInProgress = nested;
        }

        return _groundLoot.Count - before;
    }

    /// <summary>
    /// Places <paramref name="rolled" /> on the ring around <paramref name="at" />, each drop allocated by
    /// <paramref name="allocations" />, stores them and tells every connection here: a kill's drops and a doodad's alike.
    /// Its lists are <paramref name="buffers" />.
    /// </summary>
    private void PlaceAndAnnounce(Vector3 at, List<RolledDrop> rolled, KillAllocations allocations, KillBuffers buffers)
    {
        // Placed whole before any drop is stored, so a placement that throws leaves nothing on the ground.
        List<GroundLoot> drops = buffers.Placed;
        LootPlacement.Place(drops, at, rolled, allocations, GetNavigatorForPosition(at), IObject.GenerateId);

        for (int i = 0; i < drops.Count; i++)
        {
            _groundLoot.Add(drops[i]);
        }

        // Walked by index and through the dictionary's own enumerator: a kill allocates no boxed enumerator (#880).
        List<LootDropDto> dtos = buffers.DescribePlaced();
        // The same drops for every connection: encoded once, for the first, and shared by every one (#875).
        OutboundPacket spawned = default;
        foreach (IWorldConnection connection in _connections.Values)
        {
            if (spawned.Payload is null)
            {
                spawned = SLootSpawnedPacket.Create(dtos, PacketEncoder.Shared);
            }

            connection.Send(spawned.Share());
        }

        spawned.Release();
    }

    void IDoodadHost.PlayersWithin(Vector3 centre, float radius, List<CharacterEntity> into)
    {
        if (!float.IsFinite(radius) || radius < 0f)
            return;

        float radiusSquared = radius * radius;
        foreach (ICharacter character in _characters.Values)
        {
            if (character is not CharacterEntity { IsDead: false } player)
                continue;

            float dx = player.Position.x - centre.x;
            float dz = player.Position.z - centre.z;

            // Written so a distance that is not a number is out of reach.
            if (dx * dx + dz * dz <= radiusSquared)
                into.Add(player);
        }
    }

    /// <summary>
    /// Whom each of one kill's drops is for, by its place in the ring: a table drop is allocated on its own, among the
    /// characters that share the kill, or free for all at once when nobody is credited with the kill; a quest drop,
    /// which comes after the table's, belongs for good to the member it was rolled for. A struct handed to the
    /// placement by value: a lambda here cost the kill its closure and a delegate, and a Debug build a second closure
    /// (the compiler keeps one per scope there, and merges them when it optimises), which was the 32 B a kill no
    /// Release run showed (#890).
    /// </summary>
    private readonly struct KillAllocations(
        ILootAllocator? allocator,
        uint? instanceOwner,
        PartyId? instanceParty,
        IReadOnlyList<ICharacter> eligible,
        bool credited,
        int tableCount,
        IReadOnlyList<(RolledDrop Drop, uint Owner)> questDrops) : IDropAllocations
    {
        public LootAllocation For(int index) =>
            index >= tableCount
                ? new LootAllocation(questDrops[index - tableCount].Owner, DateTime.MaxValue)
                : credited
                    ? allocator!.Allocate(instanceOwner, instanceParty, eligible)
                    : allocator!.Allocate(instanceOwner: null, instanceParty: null, []);
    }

    /// <summary>
    /// The quest drops a kill rolls (#433), into the kill's own list. Contained: a throw costs this kill's quest drops,
    /// those rolled before it included, and nothing else.
    /// </summary>
    private void RollQuestDrops(ICreature creature, IReadOnlyList<ICharacter> eligible, List<(RolledDrop Drop, uint Owner)> rolled)
    {
        if (_questService is null || eligible.Count == 0)
        {
            return;
        }

        try
        {
            _questService.RollQuestDrops(creature, eligible, rolled);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Rolling quest drops for the kill of {CreatureGuid} in instance {InstanceId} failed",
                creature.Guid, InstanceId);
            rolled.Clear();
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
            // Every owed connection is sent the same snapshot: built and encoded once, for the first still here, and
            // shared by the others (#875).
            OutboundPacket snapshot = default;
            foreach (ObjectGuid guid in _lootSnapshotOwed)
            {
                if (_connections.TryGetValue(guid, out IWorldConnection? connection))
                {
                    if (snapshot.Payload is null)
                    {
                        snapshot = SLootSpawnedPacket.Create(LootDtos(_groundLoot.All), PacketEncoder.Shared);
                    }

                    connection.Send(snapshot.Share());
                }
            }

            snapshot.Release();
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

    /// <summary>The drops as the loot packet carries them: built once for every connection the drop is sent to.</summary>
    private static List<LootDropDto> LootDtos(IReadOnlyCollection<GroundLoot> drops)
    {
        var dtos = new List<LootDropDto>(drops.Count);
        if (drops is IReadOnlyList<GroundLoot> list)
        {
            for (int i = 0; i < list.Count; i++)
                dtos.Add(LootDropMapper.ToDto(list[i]));
            return dtos;
        }

        foreach (GroundLoot drop in drops)
            dtos.Add(LootDropMapper.ToDto(drop));
        return dtos;
    }

    public void Update(TimeSpan deltaTime)
    {
        if (!WorldTickTrace.Log.IsEnabled())
        {
            UpdateCore(deltaTime);
            return;
        }

        _tickProfile = new InstanceTickProfile
        {
            Enabled = true,
            StartTimestamp = Stopwatch.GetTimestamp(),
            Players = _characters.Count,
            Creatures = _creatures.Count,
        };
        long mapPackets = WorldTickTrace.MapPackets;
        bool completed = false;
        try
        {
            UpdateCore(deltaTime);
            completed = true;
        }
        finally
        {
            _tickProfile.MapPackets = WorldTickTrace.MapPackets - mapPackets;
            WorldTickTrace.Log.RecordInstance(in _tickProfile, InstanceId, (int)MapType, completed);
            _tickProfile = default;
        }
    }

    private void UpdateCore(TimeSpan deltaTime)
    {
        // First, so nothing that throws can leave the clock behind and the instance off its slot for good; and before the
        // empty check: an empty instance's clock runs too, so it is still in its slot when someone comes.
        _broadcastTick = AdvanceBroadcastClock(deltaTime);

        // The views are decided on the broadcast tick (#981); one owed by a tick that threw is still owed.
        _viewsOwed |= _broadcastTick;

        // Every hit list the last tick handed out was read within it: the hit query may reuse them (#880).
        _hits.Recycle();

        if (_characters.Count == 0)
        {
            StandStillWhileEmpty();
            return;
        }

        if (_aurasPaused)
            ResumeAuras();

        if (_healthFactorOwed)
            ApplyHealthFactor();

        // Step 0: drops already on the ground, for characters that entered since the last tick. First,
        // before any packet is processed, so a kill later in this tick reaches them once, through the
        // kill broadcast, rather than twice.
        SendOwedLootSnapshots();
        SendOwedPvpStates();

        // Step 1: remove the corpses whose BodyRemoveTimer has run out. Creatures do not respawn.
        _corpseRemover.Update(deltaTime);

        // Step 1b: summons whose lifetime has passed leave (item use), and spent doodads whose time has come are ready
        // again: the soonest is peeked at once, and nothing is read while none waits.
        DespawnExpiredSummons();
        if (_doodadRespawns.Count > 0 && _doodadUses is { } doodadUses)
            _doodadRespawns.Update(this, _time.GetUtcNow().UtcDateTime, doodadUses);

        // Step 2: Process character packets
        long profileStart = _tickProfile.Timestamp();
        UpdateCharacters(deltaTime);
        _tickProfile.InputTicks += _tickProfile.Elapsed(profileStart);

        // Step 2b: Vendors (#432). After the packets, so this tick's trades are in the lists. The
        // pass refills due stock, adopts a /reload vendors, and sends each connection whose shop is
        // open the one list it is owed. It is skipped until someone opens a shop here, so an
        // instance with no vendor state never reads vendor data.
        if (_vendors.Count > 0)
            RunVendorPass();

        // #612: one list for the life of the instance, refilled each tick, so a tick allocates none.
        _objectAbilities.Clear();

        // Step 3: abilities, combat and threat.
        profileStart = _tickProfile.Timestamp();
        UpdateCombat(deltaTime, _objectAbilities);
        _tickProfile.CombatTicks += _tickProfile.Elapsed(profileStart);

        // Step 4: creature scripts, then the locomotion that executes what they decided.
        profileStart = _tickProfile.Timestamp();
        UpdateCreatures(deltaTime);
        _tickProfile.CreatureTicks += _tickProfile.Elapsed(profileStart);

        // Step 4b: the position history of every member that moved this tick (#1082). After every move of the tick and
        // before the broadcast, so each entry is the position the broadcast stamped with this tick carries. No tick count
        // (tests), no history.
        profileStart = _tickProfile.Timestamp();
        if (_worldTicks is not null)
            RecordPositions(_worldTicks.Completed);
        _tickProfile.HistoryTicks += _tickProfile.Elapsed(profileStart);

        // Step 5a: Snapshot dirty fields — ONLY on broadcast ticks.
        profileStart = _tickProfile.Timestamp();
        SnapshotDirtyFields(_objectAbilities);
        _tickProfile.DirtyTicks += _tickProfile.Elapsed(profileStart);
        if (_tickProfile.Enabled)
        {
            _tickProfile.Broadcast = _broadcastTick;
            _tickProfile.Projectiles = _objectAbilities.Count;
            _tickProfile.DirtyEntities = _frameDirtyFields.Count;
        }

        // Step 5b and 6: visibility, then the state broadcast, per character.
        BroadcastState(_objectAbilities);
    }

    /// <summary>
    /// Step 4b of <see cref="Update" />: records where every member that moved since the last record stands at the end of
    /// world tick <paramref name="tick" /> (#1082). The grid lists them as their position setters run
    /// (<see cref="SpatialGrid.RecordMoved" />), so a unit that did not move is not read: most of a forest's creatures are
    /// waiting (#979). A unit that joined since is listed too, so its first record seeds its history, and one that never
    /// moves keeps that entry.
    /// </summary>
    private void RecordPositions(long tick) => _members.RecordMoved(tick);

    /// <summary>
    /// The history a unit joining this instance records into (#1082): the one it holds, emptied (a unit added again
    /// without leaving, or out of an instance let go with it inside), or one from the pool, empty. The grid lists the
    /// joining unit as jumped (<see cref="SpatialGrid.Add" />), so the next Step 4b seeds it wherever the unit stands then:
    /// a map arrival writes its spawn after the add, and a seed taken here would hold the coordinates of the map it left.
    /// Until then a rewound query uses the unit's present position. None in an instance with no world tick count (tests),
    /// which records nothing.
    /// </summary>
    private PositionHistory? RentHistory(PositionHistory? held)
    {
        if (_worldTicks is null)
            return held;

        PositionHistory? history = held;
        if (history is null && !_histories.TryPop(out history))
        {
            history = new PositionHistory();
            _histories.EnsureCapacity(++_historiesMade);
        }

        history.Clear();
        return history;
    }

    /// <summary>Returns a leaving unit's history to the pool (#1082); null, for the unit to hold.</summary>
    private PositionHistory? ReturnHistory(PositionHistory? history)
    {
        if (history is not null)
            _histories.Push(history);
        return null;
    }

    /// <summary>
    /// Gives the instance its place on the world's broadcast schedule (#975): its slot and the clock it starts from, from
    /// <see cref="BroadcastSchedule.Join" />. The registry calls it as it publishes the instance, on the tick, before
    /// the instance's first tick; an instance never published keeps 0, broadcasting first one interval after its first tick.
    /// </summary>
    internal void JoinBroadcastSchedule(int slot, TimeSpan phase)
    {
        BroadcastSlot = slot;
        _broadcastClock = phase.Ticks;
    }

    /// <summary>
    /// The slot <see cref="JoinBroadcastSchedule" /> gave the instance, which the registry frees when it lets the instance
    /// go; -1 while it holds none (never published, or already let go).
    /// </summary>
    internal int BroadcastSlot { get; set; } = -1;

    /// <summary>
    /// Runs the broadcast clock on by the tick's delta and says whether this tick broadcasts: the one on which the clock
    /// reaches the interval (#975). The remainder is kept rather than dropped, so the broadcasts keep to the interval, ten
    /// a second, and not to whichever tick comes after it; and an instance keeps its slot. A tick longer than the interval
    /// broadcasts once, never a burst of catch-up broadcasts on the ticks after it, and leaves the clock where its slot
    /// puts it.
    /// </summary>
    private bool AdvanceBroadcastClock(TimeSpan deltaTime)
    {
        long clock = _broadcastClock + deltaTime.Ticks;
        if (clock < BroadcastSchedule.IntervalTicks)
        {
            _broadcastClock = clock;
            return false;
        }

        _broadcastClock = clock % BroadcastSchedule.IntervalTicks;
        return true;
    }

    /// <summary>A tick with nobody here: nothing ticks, and what would wait for a player is settled now or on return.</summary>
    private void StandStillWhileEmpty()
    {
        // A projectile that finished just before the last character left would wait for a broadcast
        // that never comes, and show frozen to the next player to enter (#164). Nobody is left to send
        // its final state to, so drop it now.
        _abilityCastSystem.DropFinished();

        // Nobody is left to tell that a character left (#981); whoever comes next has been told of nothing here.
        _departedCharacters.Clear();

        // Auras stand still too; their time does not, and the first tick with players skips what came due meanwhile.
        _aurasPaused = true;
    }

    /// <summary>
    /// The first tick with players after one without: the auras' owed ticks are skipped, never paid in a burst, and those
    /// whose end passed meanwhile end. Contained, as the aura pass is.
    /// </summary>
    private void ResumeAuras()
    {
        _aurasPaused = false;
        try
        {
            _auras.SkipOwed();
        }
        catch (Exception e)
        {
            _auraPassFailures.Failed(e);
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
            {
                _pvp.Send(connection, pvpEntity);
            }

            // Periodic save (spec #459 D4): the scheduler decides whether this is the character's tick.
            if (character is CharacterEntity entity)
                _saveScheduler?.Tick(connection, entity, deltaTime);
        }
    }

    /// <summary>
    /// Step 3 of <see cref="Update" />: the cast system, the auras, the item and doodad cast bars, then the combat service,
    /// then the threat mirror.
    /// </summary>
    private void UpdateCombat(TimeSpan deltaTime, List<IWorldObject> objectAbilities)
    {
        // Step 3: Ability cast system update
        _abilityCastSystem.Update(deltaTime, objectAbilities);

        // Step 3a: auras, right after the casts that may have applied them, on this instance's clock. The pass contains
        // each aura and each unit itself; this catches anything else, so a throw never costs the instance its tick.
        try
        {
            _auras.Update();
        }
        catch (Exception e)
        {
            _auraPassFailures.Failed(e);
        }

        // Step 3b: item cast bars, then doodad cast bars (the active ones only), after this tick's movement was applied by
        // the packets above.
        _itemUses.Update(deltaTime);
        _doodadCasts.Update();

        // Step 3c: Tick combat service — decays threat, ends stale encounters.
        _combatService.Update(deltaTime);

        // Step 3d: Mirror threat lists for each player's currently-targeted hostile.
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
        UpdateCreatureScripts(deltaTime);

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

    /// <summary>
    /// The creature scripts' part of step 4: the creature clock moves on, and the scheduler runs the creatures that are
    /// active or due (#979). Bound by name by <c>CreatureDispatchBenchmarks</c>, which measures it alone.
    /// </summary>
    private void UpdateCreatureScripts(TimeSpan deltaTime)
    {
        _creatureClock += deltaTime;
        if (_scheduleCreatures)
        {
            _scheduler.Step(deltaTime, _creatureClock);
            return;
        }

        foreach (ICreature creature in _creatures.Values)
        {
            creature.Script?.Update(deltaTime);
        }
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

        if (_broadcastTick)
        {
            foreach (ICreature creature in _creatures.Values)
            {
                GameEntityFields dirty = creature.ConsumeDirtyFields();
                if (dirty != GameEntityFields.None)
                    _frameDirtyFields[creature.Guid] = dirty;
            }

            foreach (ICharacter character in _characters.Values)
            {
                GameEntityFields dirty = character.ConsumeDirtyFields();
                if (dirty != GameEntityFields.None)
                    _frameDirtyFields[character.Guid] = dirty;
            }

            foreach (IWorldObject obj in objectAbilities)
            {
                if (obj is AbilityScript ability)
                {
                    GameEntityFields dirty = ability.ConsumeDirtyFields();
                    if (dirty != GameEntityFields.None)
                        _frameDirtyFields[ability.Guid] = dirty;
                }
            }

            // Only the doodads that changed since the last broadcast (DoodadChanged), each marked once.
            for (int i = 0; i < _changedDoodads.Count; i++)
            {
                Doodad doodad = _changedDoodads[i];
                doodad.ChangeQueued = false;
                _frameDirtyFields[doodad.Guid] = GameEntityFields.DoodadState | GameEntityFields.CanInteract;
            }

            _changedDoodads.Clear();
        }
    }

    /// <summary>Steps 5b and 6 of <see cref="Update" />: every character's view first, then every broadcast.</summary>
    private void BroadcastState(List<IWorldObject> objectAbilities)
    {
        // The aura changes are forgotten even when a step below throws, so none is held over and sent again next tick.
        try
        {
            // Step 5b: every character's view, decided on the broadcast tick (#981).
            DecideViews(objectAbilities);

            // Step 6: Broadcast instance state to each character. Each entity's add and update is encoded once for all
            // of them (#980); the last broadcast's encodings are forgotten first, also when it threw. Every packet names
            // the world tick it was built on (#1066), read once here: one load, the simulation of this tick done.
            _stateTick = _worldTicks?.Completed ?? 0;
            _stateAdds.BeginBroadcast(_stateTick);
            _stateUpdates.BeginBroadcast(_stateTick);
            _shareStateEntries = _characters.Count >= StatePacketAssembler.MinRecipientsToShare;
            foreach (ICharacter character in _characters.Values)
            {
                if (character is CharacterEntity entity)
                    BroadcastStateContained(entity);
            }
        }
        finally
        {
            try
            {
                _auraUpdates.ReleaseAll();
            }
            finally
            {
                ClearAuraChanges();
            }
        }
    }

    /// <summary>
    /// One client's broadcast, contained (#981). Its view was decided before any client was sent anything, so a throw
    /// that reached the tick would leave every client after it with a view it was never told of (adds and removes lost
    /// until each object left its view and came back), and the one that threw with a view part told. The others go on;
    /// the one that threw is started over, as a move between instances starts it (#611): told to drop everything it was
    /// told of here, then listed itself and everything in view in full on the next tick, since what it was sent before the
    /// throw cannot be known.
    /// </summary>
    private void BroadcastStateContained(CharacterEntity character)
    {
        try
        {
            BroadcastStateTo(character);
        }
        catch (Exception e)
        {
            _broadcastFailures.Failed(e);
            StartOver(character);
        }
    }

    /// <summary>
    /// Forgets the client's view and tells it to drop everything it was told of, its own character included; its view is
    /// decided in full on the next tick (<see cref="CharacterCharacterGameState.Reset" />). A connection that refuses
    /// that too is logged and left: it is told nothing more until its next decision.
    /// </summary>
    private void StartOver(CharacterEntity character)
    {
        IReadOnlyList<ObjectGuid> forgotten = character.CharacterGameState.Reset();
        if (forgotten.Count == 0 || !_connections.TryGetValue(character.Guid, out IWorldConnection? connection))
            return;

        try
        {
            connection.Send(SInstanceStateRemovePacket.Create(forgotten, _replicationEncoder));
        }
        catch (Exception e)
        {
            _broadcastFailures.Failed(e);
        }
    }

    /// <summary>
    /// Step 5b of <see cref="Update" />: which objects each character's client is told exist, each by its own interest
    /// range (#593). Decided once per broadcast period, on the broadcast tick, right before the broadcast, so an object
    /// is listed or removed up to one period after it came into range or left it, and the decisions cost a sixth of what
    /// one every tick did, spread over the ticks with the broadcasts (#981, the owner's decision). Between, every view
    /// holds. Two things do not wait for the broadcast tick: a client entering (the world or this instance) is listed
    /// itself and everything in view on its first tick (<see cref="CharacterCharacterGameState.ScanOwed" />), and a
    /// character that left is removed from every client that held it on the next tick
    /// (<see cref="TellCharactersLeft" />). The replication state is World-side (#612), so a character that is not a
    /// <see cref="CharacterEntity" /> is seen by the others but is sent nothing itself.
    /// </summary>
    private void DecideViews(List<IWorldObject> objectAbilities)
    {
        bool decideAll = _viewsOwed;
        foreach (ICharacter character in _characters.Values)
        {
            if (character is not CharacterEntity entity)
                continue;

            CharacterCharacterGameState view = entity.CharacterGameState;
            if (decideAll || view.ScanOwed)
                DecideView(entity, objectAbilities);
            else
                view.Hold();
        }

        _viewsOwed = false;

        if (_departedCharacters.Count > 0)
            TellCharactersLeft();
    }

    /// <summary>
    /// Decides one client's view now (#981): its candidates come from the spatial index (#982), the cells within reach of
    /// its character, rather than from every member of the instance. Bound by name by <c>InterestVisibilityBenchmarks</c>
    /// and <c>SpatialIndexBenchmarks</c>, which measure it alone.
    /// </summary>
    private void DecideView(CharacterEntity entity, List<IWorldObject> objectAbilities)
    {
        CharacterCharacterGameState view = entity.CharacterGameState;

        // The doodads go by the viewer's own rules (its quest gates), read against the catalog of this tick; an instance
        // without doodads lists none and reads nothing.
        DoodadViewInputs doodads = _doodads.Count == 0
            ? default
            : new DoodadViewInputs(_doodads, entity, _world.Data?.Quests ?? QuestCatalog.Empty, _doodadVisibility);
        if (_viewsOverIndex)
        {
            view.UpdateProfiled(entity.Guid, entity.Position, _interest, _members, objectAbilities, doodads,
                _frameDirtyFields, ref _tickProfile);
        }
        else
        {
            view.UpdateProfiled(entity.Guid, entity.Position, _interest, _creatures, _characters, objectAbilities,
                doodads, _frameDirtyFields, ref _tickProfile);
        }
    }

    /// <summary>
    /// Decides every view by scanning every member of the instance, as before #982, rather than over the spatial index:
    /// the reference the index is compared against. For tests and benchmarks only; World-side and internal, so reached by
    /// name.
    /// </summary>
    internal void DecideViewsByFullScan() => _viewsOverIndex = false;

    /// <summary>
    /// Finds every hit by scanning every member of the instance, as before #982, rather than the cells of the spatial
    /// grid around the shape: the reference the grid is compared against. For tests and harnesses only; World-side and
    /// internal, so reached by name.
    /// </summary>
    internal void FindHitsByFullScan() => _hits.ScanEveryMember();

    /// <summary>
    /// Every character that left since the last pass is removed from every client here that still held it (#981), on the
    /// next tick as before, not at the views' next decision: a character's guid comes back with it, and a client that
    /// held it across a stay elsewhere would never hear what changed there. One that is back already (a move into the
    /// instance it was in, before this instance ticked again) is left as it is, as a decision every tick left it.
    /// </summary>
    private void TellCharactersLeft()
    {
        try
        {
            for (int i = 0; i < _departedCharacters.Count; i++)
            {
                ObjectGuid departed = _departedCharacters[i];
                if (_characters.ContainsKey(departed))
                    continue;

                foreach (ICharacter character in _characters.Values)
                {
                    if (character is CharacterEntity watcher && watcher.CharacterGameState.CharacterLeft(departed)
                        && _tickProfile.Enabled)
                    {
                        _tickProfile.Removed++;
                    }
                }
            }
        }
        finally
        {
            _departedCharacters.Clear();
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
        _statePool.Reset();

        // Indexed rather than walked with foreach, which boxes an interface's enumerator (#640).
        IReadOnlyList<ObjectGuid> newObjects = character.CharacterGameState.NewObjects;
        long profileStart;
        if (newObjects.Count > 0 && !_shareStateEntries)
        {
            SendWholeAdds(connection, character, newObjects);
        }
        else if (newObjects.Count > 0)
        {
            profileStart = _tickProfile.Timestamp();
            _stateAdds.BeginPacket();
            for (int i = 0; i < newObjects.Count; i++)
                AppendAdd(newObjects[i], character);

            if (!_stateAdds.IsEmpty)
                SendStatePacket(connection, _stateAdds.Finish(_replicationEncoder), profileStart);
            else
                _tickProfile.StateEncodingTicks += _tickProfile.Elapsed(profileStart);
        }

        // Auras: a list for every unit that came into view this tick, and the changes of every other unit in view. After
        // the add, so the client knows the unit before it hears its auras.
        profileStart = _tickProfile.Timestamp();
        SendAuraLists(connection, character, newObjects);
        SendAuraUpdates(connection, character, newObjects);
        _tickProfile.AuraTicks += _tickProfile.Elapsed(profileStart);

        // _frameDirtyFields is populated only on broadcast ticks (see Step 5a in Update),
        // so UpdatedObjects.Count > 0 already implies a broadcast cadence hit.
        IReadOnlyList<(ObjectGuid Guid, GameEntityFields Fields)> updatedObjects =
            character.CharacterGameState.UpdatedObjects;
        if (updatedObjects.Count > 0 && !_shareStateEntries)
        {
            SendWholeUpdates(connection, character, updatedObjects);
        }
        else if (updatedObjects.Count > 0)
        {
            profileStart = _tickProfile.Timestamp();
            _stateUpdates.BeginPacket();
            for (int i = 0; i < updatedObjects.Count; i++)
                AppendUpdate(updatedObjects[i], character);

            if (!_stateUpdates.IsEmpty)
                SendStatePacket(connection, _stateUpdates.Finish(_replicationEncoder), profileStart);
            else
                _tickProfile.StateEncodingTicks += _tickProfile.Elapsed(profileStart);
        }

        if (character.CharacterGameState.RemovedObjects.Count > 0)
        {
            profileStart = _tickProfile.Timestamp();
            SendStatePacket(connection, SInstanceStateRemovePacket.Create(
                character.CharacterGameState.RemovedObjects, _replicationEncoder), profileStart);
        }
    }

    /// <summary>
    /// The recipient's adds described and encoded whole, as one packet: a broadcast with fewer recipients than
    /// <see cref="StatePacketAssembler.MinRecipientsToShare" /> has nothing to share, and one encode of the list is
    /// cheaper than its entries one by one (#980).
    /// </summary>
    private void SendWholeAdds(IWorldConnection connection, CharacterEntity recipient, IReadOnlyList<ObjectGuid> added)
    {
        long start = _tickProfile.Timestamp();
        _described.Clear();
        for (int i = 0; i < added.Count; i++)
        {
            if (DescribeNewObject(added[i], recipient) is { } state)
                _described.Add(state);
        }

        SendWhole(connection, start, adds: true);
    }

    /// <summary>The recipient's updates described and encoded whole, as one packet (<see cref="SendWholeAdds" />).</summary>
    private void SendWholeUpdates(IWorldConnection connection, CharacterEntity recipient,
        IReadOnlyList<(ObjectGuid Guid, GameEntityFields Fields)> updated)
    {
        long start = _tickProfile.Timestamp();
        _described.Clear();
        for (int i = 0; i < updated.Count; i++)
        {
            if (DescribeUpdatedObject(updated[i], recipient) is { } state)
                _described.Add(state);
        }

        SendWhole(connection, start, adds: false);
    }

    /// <summary>Encodes and sends what <see cref="_described" /> holds, if anything; the describing counted as preparation.</summary>
    private void SendWhole(IWorldConnection connection, long start, bool adds)
    {
        NoteDescribed(start);
        if (_described.Count == 0)
        {
            _tickProfile.StateEncodingTicks += _tickProfile.Elapsed(start);
            return;
        }

        SendStatePacket(connection, adds
            ? SInstanceStateAddPacket.Create(_described, _stateTick, _replicationEncoder)
            : SInstanceStateUpdatePacket.Create(_described, _stateTick, _replicationEncoder), start);
    }

    /// <summary>
    /// Appends one entity's update to the recipient's packet (#980): the entry every recipient shares, encoded by the
    /// first that is sent it this broadcast, or, for the recipient's own character and for a doodad with rules, the one
    /// entry that depends on who receives it (<see cref="MaskForRecipient" />, <see cref="OwnDoodadEntry" />), described
    /// and encoded for it alone. Byte for byte the packet <see cref="SInstanceStateUpdatePacket.Create" /> wrote from the
    /// recipient's described list.
    /// </summary>
    private void AppendUpdate((ObjectGuid Guid, GameEntityFields Fields) updated, CharacterEntity recipient)
    {
        bool own = updated.Guid == recipient.Guid || OwnDoodadEntry(updated.Guid);
        if (!own && _stateUpdates.TryAppendShared(updated.Guid, updated.Fields))
            return;

        long describeStart = _tickProfile.Timestamp();
        ObjectState? state = DescribeUpdatedObject(updated, recipient);
        NoteDescribed(describeStart);

        if (own)
        {
            if (state is not null)
                _stateUpdates.AppendOwn(state);
            return;
        }

        // Described for a recipient other than the entity itself, which is how every recipient that shares it sees it.
        _stateUpdates.AppendShared(updated.Guid, updated.Fields, state);
    }

    /// <summary>
    /// Appends one entity's add to the recipient's packet (#980): the entry every recipient shares, encoded by the first
    /// that is sent it this tick, or, for the recipient's own character and for a doodad with rules, the one entry that
    /// depends on who receives it (<see cref="MaskForRecipient" />, <see cref="OwnDoodadEntry" />), described and encoded
    /// for it alone. An add describes the entity in full, so its entry is kept as one of
    /// <see cref="GameEntityFields.All" />. Byte for byte the packet <see cref="SInstanceStateAddPacket.Create" /> wrote
    /// from the recipient's described list.
    /// </summary>
    private void AppendAdd(ObjectGuid added, CharacterEntity recipient)
    {
        bool own = added == recipient.Guid || OwnDoodadEntry(added);
        if (!own && _stateAdds.TryAppendShared(added, GameEntityFields.All))
            return;

        long describeStart = _tickProfile.Timestamp();
        ObjectState? state = DescribeNewObject(added, recipient);
        NoteDescribed(describeStart);

        if (own)
        {
            if (state is not null)
                _stateAdds.AppendOwn(state);
            return;
        }

        // Described for a recipient other than the entity itself, which is how every recipient that shares it sees it.
        _stateAdds.AppendShared(added, GameEntityFields.All, state);
    }

    /// <summary>
    /// Whether <paramref name="guid" /> is a doodad with rules (<see cref="DoodadVisibility.AsksRules" />), whose entry
    /// holds the recipient's own answer to whether it can use it and so is never shared. Any other doodad's answer is what
    /// its state allows (<see cref="DoodadVisibility.Usable" />), the same for every recipient, so its entry is shared.
    /// </summary>
    private bool OwnDoodadEntry(ObjectGuid guid) =>
        guid.Type == ObjectType.Doodad && _doodads.TryGet(guid, out Doodad? doodad) && DoodadVisibility.AsksRules(doodad);

    /// <summary>
    /// Counts the describing of an entry since <paramref name="start" /> as state preparation, rather than as the
    /// encoding the rest of the packet's assembly is counted as (the tick trace).
    /// </summary>
    private void NoteDescribed(long start)
    {
        long described = _tickProfile.Elapsed(start);
        _tickProfile.StatePreparationTicks += described;
        _tickProfile.StateEncodingTicks -= described;
    }

    private void SendStatePacket(IWorldConnection connection, OutboundPacket packet, long encodingStart)
    {
        _tickProfile.StateEncodingTicks += _tickProfile.Elapsed(encodingStart);
        if (_tickProfile.Enabled)
        {
            _tickProfile.StatePackets++;
            // Read before Send transfers ownership; includes the plain packet header, not TLS or sealing.
            _tickProfile.StateBytes += packet.Size;
        }
        long enqueueStart = _tickProfile.Timestamp();
        connection.Send(packet);
        _tickProfile.EnqueueTicks += _tickProfile.Elapsed(enqueueStart);
    }

    /// <summary>
    /// A unit's whole list to a client that has just been told the unit exists (auras): for any unit holding an aura,
    /// and always for the client's own character, so an entry (the world, a portal, a respawn) replaces what it held.
    /// </summary>
    private void SendAuraLists(IWorldConnection connection, CharacterEntity recipient, IReadOnlyList<ObjectGuid> added)
    {
        for (int i = 0; i < added.Count; i++)
        {
            ObjectGuid guid = added[i];
            if (UnitHere(guid) is not { } unit || AuraHolders.Of(unit) is not { } auras)
                continue;

            if (auras.Count == 0 && guid != recipient.Guid)
                continue;

            connection.Send(SAuraListPacket.Create(guid.RawValue, _auraWire.List(auras, _time.GetUtcNow()),
                _replicationEncoder));
        }
    }

    /// <summary>
    /// This tick's aura changes of every unit the client has in view, its own character included, one packet a unit,
    /// characters first. Only the units whose auras changed are walked (<see cref="AuraRoster" />, #898). A unit it was
    /// just told about is skipped: its list carried them. A unit out of view is skipped too, and its changes are cleared
    /// with everyone's at the end of the broadcast, so a client never hears of a copy it was not listed; a unit that comes
    /// back into view is listed again. Each unit's packet is encoded once for the whole pass, by the first client that
    /// hears it, and shared with the others (<see cref="SharedAuraUpdates" />, #980). Allocates nothing while no unit
    /// changed.
    /// </summary>
    private void SendAuraUpdates(IWorldConnection connection, CharacterEntity recipient, IReadOnlyList<ObjectGuid> added)
    {
        IReadOnlyList<IUnit> characters = _auras.Roster.ChangedCharacters;
        for (int i = 0; i < characters.Count; i++)
            SendAuraUpdate(connection, recipient, characters[i], added);

        IReadOnlyList<IUnit> creatures = _auras.Roster.ChangedCreatures;
        for (int i = 0; i < creatures.Count; i++)
            SendAuraUpdate(connection, recipient, creatures[i], added);
    }

    private void SendAuraUpdate(IWorldConnection connection, CharacterEntity recipient, IUnit unit,
        IReadOnlyList<ObjectGuid> added)
    {
        if (AuraHolders.Of(unit) is not { HasChanges: true } auras)
            return;

        if (unit.Guid != recipient.Guid && !recipient.CharacterGameState.Knows(unit.Guid))
            return;

        for (int i = 0; i < added.Count; i++)
        {
            if (added[i] == unit.Guid)
                return;
        }

        connection.Send(_auraUpdates.For(unit, auras));
    }

    /// <summary>
    /// Every unit's changes have gone to whoever sees it: forgotten, so each is sent once and none waits for a watcher
    /// that never comes. Only the units that changed are walked (<see cref="AuraRoster" />, #898).
    /// </summary>
    private void ClearAuraChanges() => _auras.Roster.ClearChanges();

    private IUnit? UnitHere(ObjectGuid guid) =>
        _characters.TryGetValue(guid, out ICharacter? character) ? character
        : _creatures.TryGetValue(guid, out ICreature? creature) ? creature
        : null;

    /// <summary>
    /// An entity the recipient has not seen before, described in full. Null when the entity
    /// has left the instance between being noticed and being described. Portals are never tracked, so
    /// never described here: a client gets them with the map, in SChunkLayoutPacket.Portals (#612).
    /// </summary>
    private ObjectState? DescribeNewObject(ObjectGuid guid, CharacterEntity recipient)
    {
        switch (guid.Type)
        {
            case ObjectType.Character:
                if (!_characters.TryGetValue(guid, out ICharacter? addedCharacter))
                    return null;
                return ObjectStateWriter.From(
                    addedCharacter,
                    MaskForRecipient(GameEntityFields.All, guid, recipient.Guid),
                    _statePool);

            case ObjectType.Creature:
                if (!_creatures.TryGetValue(guid, out ICreature? addedCreature))
                    return null;
                return ObjectStateWriter.From(addedCreature, GameEntityFields.All, _statePool);

            case ObjectType.SpellProjectile:
                IWorldObject? addedAbility = _abilityCastSystem.GetAbility(guid);
                return addedAbility is null ? null : ObjectStateWriter.From(addedAbility, _statePool);

            case ObjectType.Doodad:
                // A despawned doodad is never described: it is removed from every view instead.
                if (!_doodads.TryGet(guid, out Doodad? addedDoodad) || !addedDoodad.Visible)
                    return null;
                return ObjectStateWriter.From(addedDoodad, GameEntityFields.All,
                    recipient.CharacterGameState.CanInteractWith(addedDoodad), _statePool);

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
        CharacterEntity recipient)
    {
        switch (updatedObject.Guid.Type)
        {
            case ObjectType.Character:
                if (!_characters.TryGetValue(updatedObject.Guid, out ICharacter? updatedCharacter))
                    return null;
                GameEntityFields fields = MaskForRecipient(updatedObject.Fields, updatedObject.Guid, recipient.Guid);
                // None is a sentinel bit, not zero. Motion alone has nothing left for its owner, whose ACK
                // carries it, and experience alone nothing for anyone else (#993): no entry, and a packet
                // left with no entry is not sent. Gameplay changes (including PvP turning off) must still
                // produce a state.
                if ((fields & ~GameEntityFields.None) == 0)
                    return null;
                return ObjectStateWriter.From(updatedCharacter, fields, _statePool);

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

            case ObjectType.Doodad:
                // Its state and the recipient's answer are all that change: a mark of neither has nothing to say, and a
                // despawned doodad is removed rather than described.
                if ((updatedObject.Fields & (GameEntityFields.DoodadState | GameEntityFields.CanInteract)) == 0
                    || !_doodads.TryGet(updatedObject.Guid, out Doodad? updatedDoodad) || !updatedDoodad.Visible)
                {
                    return null;
                }

                return ObjectStateWriter.From(updatedDoodad, updatedObject.Fields,
                    recipient.CharacterGameState.CanInteractWith(updatedDoodad), _statePool);

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
        // Encoded once, for the first who hears it, and shared by everyone who does (#875).
        OutboundPacket animation = default;
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, attacker.Guid, null, attacker.Position, null))
            {
                continue;
            }

            if (animation.Payload is null)
            {
                animation = SUnitAttackAnimationPacket.Create(attacker.Guid, animationId,
                    PacketEncoder.Shared);
            }

            connection.Send(animation.Share());
        }

        animation.Release();
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

        // Encoded once, for the first who hears it, and shared by everyone who does (#875).
        OutboundPacket finish = default;
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, attacker.Guid, null, attacker.Position, null))
            {
                continue;
            }

            if (finish.Payload is null)
            {
                finish = SUnitFinishCastPacket.Create(attacker.Guid, spell.AbilityId, castId,
                    PacketEncoder.Shared);
            }

            connection.Send(finish.Share());
        }

        finish.Release();
    }

    public void BroadcastInterruptedCast(IUnit attacker, IAbility spell) => BroadcastInterruptedCast(attacker, spell, 0u);

    public void BroadcastInterruptedCast(IUnit attacker, IAbility spell, uint castId)
    {
        if (!_creatures.ContainsKey(attacker.Guid) && !_characters.ContainsKey(attacker.Guid))
        {
            return;
        }

        // Encoded once, for the first who hears it, and shared by everyone who does (#875).
        OutboundPacket interrupted = default;
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, attacker.Guid, null, attacker.Position, null))
            {
                continue;
            }

            if (interrupted.Payload is null)
            {
                interrupted = SCharacterInterruptedCastPacket.Create(attacker.Guid, spell.AbilityId, castId,
                    PacketEncoder.Shared);
            }

            connection.Send(interrupted.Share());
        }

        interrupted.Release();
    }

    void IItemCastAudience.BroadcastItemCastStart(IUnit caster, ItemTemplateId item, float castTimeSeconds, uint castId)
    {
        if (!_characters.ContainsKey(caster.Guid)) return;

        // Encoded once, for the first who hears it, and shared by everyone who does (#875).
        OutboundPacket start = default;
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, caster.Guid, null, caster.Position, null)) continue;
            if (start.Payload is null)
            {
                // #1082: the tick the bar started on, as an ability's start carries it.
                start = SUnitStartCastPacket.CreateForItem(caster.Guid, castTimeSeconds, item.Value, castId,
                    _worldTicks?.Completed ?? 0, PacketEncoder.Shared);
            }

            connection.Send(start.Share());
        }

        start.Release();
    }

    void IItemCastAudience.BroadcastItemCastFinish(IUnit caster, ItemTemplateId item, uint castId)
    {
        if (!_characters.ContainsKey(caster.Guid)) return;

        // Encoded once, for the first who hears it, and shared by everyone who does (#875).
        OutboundPacket finish = default;
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, caster.Guid, null, caster.Position, null)) continue;
            if (finish.Payload is null)
            {
                finish = SUnitFinishCastPacket.CreateForItem(caster.Guid, item.Value, castId, PacketEncoder.Shared);
            }

            connection.Send(finish.Share());
        }

        finish.Release();
    }

    void IItemCastAudience.BroadcastItemCastInterrupted(IUnit caster, ItemTemplateId item, uint castId)
    {
        if (!_characters.ContainsKey(caster.Guid)) return;

        // Encoded once, for the first who hears it, and shared by everyone who does (#875).
        OutboundPacket interrupted = default;
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, caster.Guid, null, caster.Position, null)) continue;
            if (interrupted.Payload is null)
            {
                interrupted = SCharacterInterruptedCastPacket.CreateForItem(caster.Guid, item.Value, castId,
                    PacketEncoder.Shared);
            }

            connection.Send(interrupted.Share());
        }

        interrupted.Release();
    }

    void IDoodadCastAudience.BroadcastDoodadCastStart(IUnit caster, Doodad doodad, float castTimeSeconds, uint castId)
    {
        if (!_characters.ContainsKey(caster.Guid)) return;

        // Encoded once, for the first who hears it, and shared by everyone who does (#875).
        OutboundPacket start = default;
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!HearsDoodadCast(guid, connection, caster, doodad)) continue;
            if (start.Payload is null)
            {
                // The tick the bar started on, as an ability's and an item's start carry it (#1082).
                start = SUnitStartCastPacket.CreateForDoodad(caster.Guid, castTimeSeconds, doodad.Template.Id.Value,
                    castId, _worldTicks?.Completed ?? 0, PacketEncoder.Shared);
            }

            connection.Send(start.Share());
        }

        start.Release();
    }

    void IDoodadCastAudience.BroadcastDoodadCastFinish(IUnit caster, Doodad doodad, uint castId)
    {
        if (!_characters.ContainsKey(caster.Guid)) return;

        // Encoded once, for the first who hears it, and shared by everyone who does (#875).
        OutboundPacket finish = default;
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!HearsDoodadCast(guid, connection, caster, doodad)) continue;
            if (finish.Payload is null)
            {
                finish = SUnitFinishCastPacket.CreateForDoodad(caster.Guid, doodad.Template.Id.Value, castId,
                    PacketEncoder.Shared);
            }

            connection.Send(finish.Share());
        }

        finish.Release();
    }

    void IDoodadCastAudience.BroadcastDoodadCastInterrupted(IUnit caster, Doodad doodad, uint castId)
    {
        if (!_characters.ContainsKey(caster.Guid)) return;

        // Encoded once, for the first who hears it, and shared by everyone who does (#875).
        OutboundPacket interrupted = default;
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!HearsDoodadCast(guid, connection, caster, doodad)) continue;
            if (interrupted.Payload is null)
            {
                interrupted = SCharacterInterruptedCastPacket.CreateForDoodad(caster.Guid, doodad.Template.Id.Value,
                    castId, PacketEncoder.Shared);
            }

            connection.Send(interrupted.Share());
        }

        interrupted.Release();
    }

    /// <summary>
    /// Whether a connection hears a doodad's cast bar, which names the doodad's template: the user always; anyone else
    /// near it as an item's cast bar is heard (<see cref="Hears" />) only when its view holds the doodad, so a doodad
    /// hidden from a player never reaches it through another's cast. One lookup per listener, as the bar starts and ends.
    /// </summary>
    private bool HearsDoodadCast(ObjectGuid guid, IWorldConnection connection, IUnit caster, Doodad doodad) =>
        guid == caster.Guid
        || (Hears(guid, connection, caster.Guid, null, caster.Position, null)
            && connection.Character is CharacterEntity listener && listener.CharacterGameState.Knows(doodad.Guid));

    /// <summary>The doodad cast owner of an instance built without the use service, which never starts a doodad cast.</summary>
    private sealed class NoDoodadCastOwner : IDoodadCastOwner
    {
        public static readonly NoDoodadCastOwner Instance = new();

        public bool CanComplete(IDoodadHost host, in PendingDoodadUse use) => false;

        public void Completed(IDoodadHost host, in PendingDoodadUse use)
        {
        }

        public void Interrupted(IDoodadHost host, in PendingDoodadUse use)
        {
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
                character.CurrentHealth, damage, abilityId?.Value, PacketEncoder.Shared, result));
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

        // Encoded once, for the first who hears it, and shared by everyone who does (#875).
        OutboundPacket healed = default;
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, healer.Guid, target.Guid, target.Position, null))
            {
                continue;
            }

            if (healed.Payload is null)
            {
                healed = SUnitHealedPacket.Create(healer.Guid.RawValue, target.Guid.RawValue, restored,
                    target.CurrentHealth, abilityId?.Value, result, PacketEncoder.Shared);
            }

            connection.Send(healed.Share());
        }

        healed.Release();
    }

    /// <summary>
    /// An aura's tick (auras), to everyone who hears it (#532), as a hit or a heal is, naming its aura: a character hurt
    /// is sent its own damage packet first. Nobody applied it, or its caster is gone: the packets name raw 0. Only for a
    /// target in this instance, as a hit is.
    /// </summary>
    void ICombatOutcomes.PeriodicTick(IUnit? caster, IUnit target, uint amount, AuraId aura, HitResult result, bool heal)
    {
        if (!_characters.ContainsKey(target.Guid) && !_creatures.ContainsKey(target.Guid))
        {
            return;
        }

        // A tick whose caster is gone is from nobody: one shared empty guid, only read, rather than a new one per tick (#880).
        ObjectGuid from = caster?.Guid ?? s_nobody;
        if (!heal && target is CharacterEntity character && _connections.TryGetValue(character.Guid, out IWorldConnection? own))
        {
            own.Send(SCharacterDamagePacket.Create(from.RawValue, character.Guid.RawValue, character.CurrentHealth, amount,
                null, PacketEncoder.Shared, result, aura.Value));
        }

        // Encoded once, for the first who hears it, and shared by everyone who does (#875).
        OutboundPacket tick = default;
        foreach ((ObjectGuid guid, IWorldConnection connection) in _connections)
        {
            if (!Hears(guid, connection, target.Guid, caster?.Guid, target.Position, null))
            {
                continue;
            }

            if (tick.Payload is null)
            {
                tick = heal
                    ? SUnitHealedPacket.Create(from.RawValue, target.Guid.RawValue, amount, target.CurrentHealth, null, result,
                        PacketEncoder.Shared, aura.Value)
                    : SUnitDamagePacket.Create(from, target.Guid.RawValue, target.CurrentHealth, amount,
                        PacketEncoder.Shared, result, aura.Value);
            }

            connection.Send(tick.Share());
        }

        tick.Release();
    }

    /// <summary>
    /// Death ends every aura (auras). Reached again for a unit already dead, and from inside an aura's own tick, so it
    /// ends only what is still held. Contained and throttled: a failure costs the auras, never the death that follows;
    /// what it leaves on the corpse the next aura pass ends.
    /// </summary>
    void ICombatOutcomes.UnitDied(IUnit unit)
    {
        try
        {
            _auras.RemoveAll(unit, AuraRemoveReason.Death);
        }
        catch (Exception e)
        {
            _auraDeathFailures.Failed(e);
        }
    }

    HitOutcome IAbilityArena.DamageForAbility(IUnit caster, IUnit target, IAbility ability) =>
        _combatService.ApplyDamageWithOutcome(caster, target, ability.Metadata.EffectValue, ability);

    void IAbilityArena.HealForAbility(IUnit caster, IUnit target, IAbility ability) =>
        _combatService.ApplyHeal(caster, target, ability.Metadata.EffectValue, ability);

    /// <summary>
    /// The aura the ability's live row names, on <paramref name="target" />; who may receive it the shape and Hostility
    /// already decided. Contained: an application that throws (a stats refresh or a hook's removal inside it) is logged,
    /// throttled, and costs the cast none of its other targets.
    /// </summary>
    void IAbilityArena.ApplyAbilityAura(IUnit caster, IUnit target, IAbility ability)
    {
        if (_world.Data?.LoadedAbilities is not { } abilities
            || !abilities.TryGet(ability.AbilityId, out AbilityTemplate? row)
            || row.AuraId is not { } aura)
        {
            return;
        }

        try
        {
            _auras.Apply(caster, target, aura, AuraSource.Of(ability));
        }
        catch (Exception e)
        {
            _auraApplyFailures.Failed(e);
        }
    }

    void ICombatOutcomes.CreatureKilled(ICreature creature, IUnit? killer)
    {
        if (!_creatures.ContainsKey(creature.Guid))
        {
            return;
        }

        bool nested = _killInProgress;
        KillBuffers buffers = nested ? new KillBuffers() : _killBuffers;
        _killInProgress = true;
        try
        {
            // Taken first, once, while the creature is still in its encounter (spec 2026-09-30 section 4). Contained:
            // a throw here must not leave a creature at 0 health with its script and no corpse teardown. Nobody is
            // eligible then: nobody gains experience from it, and a party instance's drops are free for all.
            List<ICharacter> eligible = buffers.Eligible;
            try
            {
                FillEligible(eligible, buffers.MemberKey, creature, killer);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Working out who shares the kill of {CreatureGuid} in instance {InstanceId} failed",
                    creature.Guid, InstanceId);
                eligible.Clear();
            }

            creature.Script = null;
            TearDownCorpse(creature);
            _corpseRemover.ScheduleRemoval(creature);

            // #433: kill credit for everyone who shares the kill, before the drops, which read the same quests.
            CreditQuests(creature, eligible);

            // Whatever killed it: loot does not depend on the killer being a character. A solo instance's drops
            // still go to its owner and a town's are free for all; only a party instance draws among the eligible.
            // A kill nobody is credited with (an aura's tick whose caster is gone) drops everything free for all.
            DropLoot(creature, eligible, credited: killer is not null, buffers);
            AwardExperience(creature, eligible, buffers.Shares);
        }
        finally
        {
            buffers.Clear();
            _killInProgress = nested;
        }
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
    /// Adds the characters that share this kill to <paramref name="eligible" />: the killer alone outside a party, or
    /// every present, eligible member of its party. None when there is no killer, or it is not a character or is in a
    /// leave countdown.
    /// </summary>
    private void FillEligible(List<ICharacter> eligible, ObjectGuid memberKey, ICreature creature, IUnit? killer)
    {
        var character = killer as ICharacter;
        Party? party = character is null ? null : _parties?.PartyOf(character.Guid.Id);

        PartyEligibility.Fill(eligible, character, party, _characters, _inCountdown,
            _combatService.GetEncounterFor(creature), creature.Position, _world.Configuration.PartyEligibilityRange,
            memberKey);
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
    /// The split leaves out anyone the level gap or more above the creature, or who can gain no experience (at the maximum
    /// level or before a gap in the levels, #735), solo too. <paramref name="shares" /> is the kill's own empty list.
    /// </summary>
    private void AwardExperience(ICreature creature, IReadOnlyList<ICharacter> eligible, List<ExperienceShare> shares)
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
        PartyExperience.Fill(shares, creature.Experience, creature.Level, eligible, mode,
            config.PartyExperienceBonusPerExtra, config.PartyExperienceLevelGap, _canGainExperience);

        for (int i = 0; i < shares.Count; i++)
        {
            AwardExperience(shares[i].Member, shares[i].Experience);
        }
    }

    /// <summary>
    /// Gives a character its share of a kill's experience, scaled by this map's level band, through the one level-up
    /// helper quests use too (#433): it levels past every requirement the award covers, up to the maximum level or the
    /// level before a gap. A level with no requirement awards nothing.
    /// </summary>
    private void AwardExperience(ICharacter character, uint experience) =>
        ExperienceAward.Grant(character, ScaledExperience(character, experience), _world.Data, _parties, _logger);

    /// <summary>This character's share of the kill's experience, scaled by this map's level band.</summary>
    private uint ScaledExperience(ICharacter character, uint experience)
    {
        MapTemplate? mapTemplate = BandTemplate();

        double bandScale = BandScale(
            character.Level,
            mapTemplate?.MinLevel,
            mapTemplate?.MaxLevel,
            _world.Configuration.ExperienceBandDecay);

        return (uint)Math.Round(experience * bandScale, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// This map's template among the world's, or null when the world has none for it: found once and kept, where every
    /// share of every kill walked the world's list (#883), and found again should the world hold another list.
    /// </summary>
    private MapTemplate? BandTemplate()
    {
        IReadOnlyList<MapTemplate> templates = _world.MapTemplates;
        if (ReferenceEquals(templates, _bandTemplatesFrom))
        {
            return _bandTemplate;
        }

        // A plain walk: a lambda over this instance allocated its delegate (#880).
        MapTemplate? found = null;
        for (int i = 0; i < templates.Count; i++)
        {
            if (templates[i].Id == TemplateId)
            {
                found = templates[i];
                break;
            }
        }

        _bandTemplatesFrom = templates;
        _bandTemplate = found;
        return found;
    }

    /// <summary>
    /// A character's motion, which its own client is never sent on the state broadcast: its input ACK
    /// (<c>SPlayerStateAckPacket</c>) carries it.
    /// </summary>
    private const GameEntityFields SelfSuppressedFields =
        GameEntityFields.Position | GameEntityFields.Velocity | GameEntityFields.Orientation;

    /// <summary>
    /// A character's experience and the experience its next level needs, which only its own client is sent (#993): the
    /// client reads them for its own experience bar alone, and nobody else is shown another character's progress.
    /// </summary>
    private const GameEntityFields OwnerOnlyFields = GameEntityFields.Experience | GameEntityFields.RequiredExperience;

    /// <summary>
    /// The fields a character's add or update is described with for one recipient. To the character's own client, its
    /// motion is left out (<see cref="SelfSuppressedFields" />) and the rest, its experience included, rides the
    /// broadcast. To every other client, its experience is left out (<see cref="OwnerOnlyFields" />, #993). So a
    /// character's entry is the same for every recipient but itself, which is what lets the broadcast share it (#980),
    /// and its own entry, described for its client alone, is the only one that carries its experience.
    /// </summary>
    public static GameEntityFields MaskForRecipient(GameEntityFields fields, ObjectGuid subjectGuid, ObjectGuid recipientGuid) =>
        subjectGuid == recipientGuid ? fields & ~SelfSuppressedFields : fields & ~OwnerOnlyFields;
}
