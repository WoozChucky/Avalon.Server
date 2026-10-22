using System.Collections.Immutable;
using System.Linq.Expressions;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.GameAuth;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Domain;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Hosting.Networking;
using Avalon.Hosting.Telemetry;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.World;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Maps.Navigation;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Respawn;
using Avalon.World.Scripts;
using Avalon.World.Testing.Scenarios;
using Avalon.World.Trading;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// One arrival in a map through the code that handles it, up to the last packet it sends (#883):
/// <see cref="Portal_entry_into_the_forest" /> and <see cref="Portal_entry_into_the_town" /> are one
/// <c>CMSG_ENTER_MAP</c> through <c>EnterMapHandler</c> and its arrival continuation, <see cref="Return_to_town" /> one
/// <c>TownReturn</c> (a respawn, a scroll, a party's leave countdown), and <see cref="Character_select" /> one
/// <c>CMSG_CHARACTER_SELECTED</c> through the whole select chain to the pending spawn. The <c>Refused_</c> rows are a
/// <c>CMSG_ENTER_MAP</c> the handler refuses, each of which a client can send as often as it likes: from a dead
/// character (dropped, no reply), while another move is under way, for a map that does not exist, for a map the
/// current one has no portal to, and from too far away from the portal.
/// </summary>
/// <remarks>
/// <para>
/// The instances are real: the town in the layout the World server builds from <c>Maps/TownLayouts/1.json</c> (four
/// chunks, its portal to the forest and its scenery ring) and a forest generated on <see cref="ForestSeed" /> by the
/// World server's own generator, ringed from the committed pool. Every row here arrives in an instance others have
/// arrived in before; <see cref="FirstArrivalBenchmarks" /> arrives in one nobody has entered, which is every entry of
/// a character's own forest outside its re-entry window.
/// </para>
/// <para>
/// Everything around the handlers is hand-written and holds its answers, so a row is the handler and what it sends:
/// the lookups, the checks, the transition and the chunk layout. The transfer only changes the character's instance
/// id (<c>World.TransferPlayer</c>'s membership change is not here), a save is a task already finished, and a select's
/// database reads are finished tasks over an empty inventory and no abilities. The select's row therefore also holds
/// the character it builds.
/// </para>
/// <para>
/// Nothing a row does to put the character back where it starts allocates: the character's instance is written on
/// its row as text each instance holds ready, where <c>CharacterEntity.InstanceId</c>'s setter would format the id
/// again (96 B) on every call, in the reset and once more in the transfer.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class ArrivalBenchmarks
{
    private const int ForestSeed = 7;

    private static readonly AccountId s_account = new(42L);

    private ScenarioWorld _scenario = null!;
    private ArrivalWorld _world = null!;
    private ServiceProvider _instanceServices = null!;
    private ArrivalInstance _town = null!;
    private ArrivalInstance _forest = null!;
    private Vector3 _townPortal;
    private Vector3 _forestPortal;
    private CEnterMapPacket _toForest = null!;
    private CEnterMapPacket _toTown = null!;
    private CEnterMapPacket _toNowhere = null!;
    private CCharacterSelectedPacket _selected = null!;

    private Character _row = null!;
    private CharacterEntity _character = null!;
    private ArrivalConnection _connection = null!;
    private ArrivalRegistry _registry = null!;
    private EnterMapHandler _enterMap = null!;
    private TownReturn _townReturn = null!;
    private CharacterSelectHandler _select = null!;

    [GlobalSetup]
    public void Setup()
    {
        _scenario = ScenarioWorld.CreateWithReferenceData();
        ScenarioReferenceData reference = ScenarioReferenceData.Shared;
        MapTemplate forestTemplate = reference.ForestTemplate;
        MapTemplate townTemplate = reference.TownTemplate;
        ushort townMap = townTemplate.Id.Value;

        _forest = new ArrivalInstance(_scenario.NewForestInstance(ForestSeed));
        _forestPortal = PortalTo(_forest.Instance, townMap);

        // What an instance built here resolves as it is constructed: the chunk library it names its layout's chunks
        // from, as the server's container hands it one.
        var services = new ServiceCollection();
        services.AddSingleton(new CombatConfig());
        services.AddSingleton<IScriptManager>(new ScriptManager(NullLoggerFactory.Instance));
        services.AddSingleton(reference.Chunks);
        _instanceServices = services.BuildServiceProvider();

        // The templates as the map manager holds them: with their lookup by id.
        ArrivalWorld world = _world = new ArrivalWorld(reference.Data, MapTemplateRows.Keyed([townTemplate, forestTemplate]));
        _town = new ArrivalInstance(Build(townTemplate.Id, reference.TownLayout, MapType.Town));
        _townPortal = PortalTo(_town.Instance, forestTemplate.Id.Value);
        _registry = world.Registry;
        _registry.Town = _town;
        _registry.Forest = _forest;

        _toForest = new CEnterMapPacket { TargetMapId = forestTemplate.Id.Value };
        _toTown = new CEnterMapPacket { TargetMapId = townMap };
        _toNowhere = new CEnterMapPacket { TargetMapId = ushort.MaxValue };
        _selected = new CCharacterSelectedPacket { CharacterId = 1 };

        _row = new Character
        {
            Id = new CharacterId(1),
            AccountId = s_account,
            Name = "Bench",
            Class = CharacterClass.Warrior,
            Level = (ushort)Math.Max(1, (int)(forestTemplate.MinLevel ?? 1)),
            Map = townMap,
            CreationDate = DateTime.UtcNow,
        };
        _character = new CharacterEntity(NullLoggerFactory.Instance, _row, new RegenConfiguration()) { Data = _row };
        _connection = new ArrivalConnection(s_account) { Character = _character };

        var saver = new FinishedSaver();
        var resolver = new TownResolver(townTemplate.Id);
        _enterMap = new EnterMapHandler(NullLogger<EnterMapHandler>.Instance, saver, world);
        _townReturn = new TownReturn(NullLogger.Instance, world, resolver, new InlineWork());
        _select = new CharacterSelectHandler(
            NullLogger<CharacterSelectHandler>.Instance,
            NullLoggerFactory.Instance,
            new HeldCharacters(_row),
            new NoSlots(),
            new NoItems(),
            new NoAbilities(),
            world,
            resolver,
            Options.Create(new RegenConfiguration()),
            new HeldAccounts(new Account
            {
                Id = s_account,
                Username = "BENCH",
                Salt = [],
                Verifier = [],
                Email = null,
                JoinDate = DateTime.UtcNow,
            }),
            saver,
            new NoOtherSessions(),
            databaseWork: new InlineWork());

        // Each row once, checked: every one must arrive, and send what an arrival sends.
        Expect(Portal_entry_into_the_forest, sent: 2, in_: _forest.Instance);
        Expect(Portal_entry_into_the_town, sent: 2, in_: _town.Instance);
        Expect(Return_to_town, sent: 2, in_: _town.Instance);

        // A refusal answers once and moves nobody; a dead character's request is dropped unanswered.
        Refuse(Refused_while_a_move_is_under_way, sent: 1);
        Refuse(Refused_for_an_unknown_map, sent: 1);
        Refuse(Refused_without_a_portal, sent: 1);
        Refuse(Refused_too_far_from_the_portal, sent: 1);
        Refuse(Refused_from_a_dead_character, sent: 0);

        int before = _connection.Sent;
        Character_select();
        // The selected packet, the layout, the inventory snapshot, the abilities and the ignore list.
        if (_connection.Sent - before != 5 || _connection.LastPending?.Instance != _town.Instance)
            throw new InvalidOperationException($"The select sent {_connection.Sent - before} packets, or left no pending spawn in the town.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _town.Instance.Dispose();
        _scenario.Dispose();
        _instanceServices.Dispose();
    }

    /// <summary>
    /// A new instance of the forest's layout, as the registry hands one out, that nobody has arrived in: what a
    /// character's own forest is on every entry outside its re-entry window. For <see cref="FirstArrivalBenchmarks" />.
    /// </summary>
    internal ArrivalInstance NewForest() =>
        new(Build(_forest.Instance.TemplateId, _forest.Instance.Layout, MapType.Normal));

    /// <summary>A new instance of the town's layout that nobody has arrived in. For <see cref="FirstArrivalBenchmarks" />.</summary>
    internal ArrivalInstance NewTown() => new(Build(_town.Instance.TemplateId, _town.Instance.Layout, MapType.Town));

    /// <summary><see cref="Portal_entry_into_the_forest" />, into <paramref name="forest" /> in place of the forest kept here.</summary>
    internal int EnterForest(ArrivalInstance forest)
    {
        _registry.Forest = forest;
        int sent = Portal_entry_into_the_forest();
        _registry.Forest = _forest;
        return sent;
    }

    /// <summary>
    /// A new instance of the forest's layout whose build the registry has not published yet: a request for it is
    /// answered with a task that is not finished, completed as the registry completes its own (its continuations
    /// run on the thread pool). For <see cref="FirstArrivalBenchmarks" />.
    /// </summary>
    internal ArrivalInstance NewForestBeingBuilt() =>
        new(Build(_forest.Instance.TemplateId, _forest.Instance.Layout, MapType.Normal),
            new TaskCompletionSource<IMapInstance>(TaskCreationOptions.RunContinuationsAsynchronously));

    /// <summary>
    /// A portal entry into <paramref name="forest" />, whose build is not published when the request is handled: the
    /// request, then the build's publication, then the flush that runs the arrival, which waits here for the
    /// continuation the connection was handed to be ready, as the tick's flush finds it ready or leaves it for the
    /// next tick.
    /// </summary>
    internal int EnterForestBeingBuilt(ArrivalInstance forest)
    {
        _registry.Forest = forest;
        Stand(_town, _townPortal);
        _enterMap.Execute(_connection, _toForest);
        forest.Publish();
        _connection.RunHeld();
        _registry.Forest = _forest;
        return _connection.Sent;
    }

    /// <summary><see cref="Portal_entry_into_the_town" />, into <paramref name="town" /> in place of the town kept here.</summary>
    internal int EnterTown(ArrivalInstance town)
    {
        _registry.Town = town;
        int sent = Portal_entry_into_the_town();
        _registry.Town = _town;
        return sent;
    }

    [Benchmark]
    public int Portal_entry_into_the_forest()
    {
        Stand(_town, _townPortal);
        _enterMap.Execute(_connection, _toForest);
        return _connection.Sent;
    }

    [Benchmark]
    public int Portal_entry_into_the_town()
    {
        Stand(_forest, _forestPortal);
        _enterMap.Execute(_connection, _toTown);
        return _connection.Sent;
    }

    [Benchmark]
    public int Return_to_town()
    {
        Stand(_forest, _forestPortal);
        _connection.RespawnInFlight = true;
        _townReturn.Start(_connection, revive: false, dropEncounter: true);
        return _connection.Sent;
    }

    [Benchmark]
    public int Character_select()
    {
        _connection.Character = null;
        _row.Map = _town.Instance.TemplateId.Value;
        _select.Execute(_connection, _selected);
        _connection.EndSelect();
        _connection.Character = _character;
        return _connection.Sent;
    }

    [Benchmark]
    public int Refused_from_a_dead_character()
    {
        Stand(_town, _townPortal);
        _character.IsDead = true;
        _enterMap.Execute(_connection, _toForest);
        _character.IsDead = false;
        return _connection.Sent;
    }

    [Benchmark]
    public int Refused_while_a_move_is_under_way()
    {
        Stand(_town, _townPortal);
        _connection.RespawnInFlight = true;
        _enterMap.Execute(_connection, _toForest);
        _connection.RespawnInFlight = false;
        return _connection.Sent;
    }

    [Benchmark]
    public int Refused_for_an_unknown_map()
    {
        Stand(_town, _townPortal);
        _enterMap.Execute(_connection, _toNowhere);
        return _connection.Sent;
    }

    [Benchmark]
    public int Refused_without_a_portal()
    {
        // The town has no portal to itself.
        Stand(_town, _townPortal);
        _enterMap.Execute(_connection, _toTown);
        return _connection.Sent;
    }

    [Benchmark]
    public int Refused_too_far_from_the_portal()
    {
        Stand(_town, _town.Instance.EntrySpawnWorldPos);
        _enterMap.Execute(_connection, _toForest);
        return _connection.Sent;
    }

    /// <summary>Puts the character where a row starts: in <paramref name="where" />, at <paramref name="at" />.</summary>
    private void Stand(ArrivalInstance where, Vector3 at)
    {
        _row.Map = where.Instance.TemplateId.Value;
        _row.InstanceId = where.IdText;
        _character.Position = at;
    }

    private void Expect(Func<int> row, int sent, MapInstance in_)
    {
        int before = _connection.Sent;
        row();
        if (_connection.Sent - before != sent || _character.InstanceId != in_.InstanceId || _connection.RespawnInFlight)
        {
            throw new InvalidOperationException(
                $"{row.Method.Name} sent {_connection.Sent - before} packets and left the character in instance {_character.InstanceId}.");
        }
    }

    private void Refuse(Func<int> row, int sent)
    {
        int before = _connection.Sent;
        row();
        if (_connection.Sent - before != sent || _character.InstanceId != _town.Instance.InstanceId || _connection.RespawnInFlight
            || _character.IsDead)
        {
            throw new InvalidOperationException($"{row.Method.Name} sent {_connection.Sent - before} packets, or moved the character.");
        }
    }

    private static Vector3 PortalTo(MapInstance instance, ushort map)
    {
        foreach (PortalInstance portal in instance.Portals)
        {
            if (portal.TargetMapId == map)
                return portal.Position;
        }

        throw new InvalidOperationException($"Instance of map {instance.TemplateId.Value} has no portal to map {map}.");
    }

    /// <summary>
    /// An instance of <paramref name="layout" /> with the portals the layout places and no creatures: an arrival reads
    /// its layout, its entry spawn and its portals. Its constructor builds the layout's packet entries, as the server's
    /// instance factory has it do off the tick.
    /// </summary>
    private MapInstance Build(MapTemplateId template, ChunkLayout layout, MapType mapType)
    {
        var instance = new MapInstance(NullLoggerFactory.Instance, _instanceServices, _world, template,
            ownerCharacterId: null, layout, new MapNavigator(NullLoggerFactory.Instance), layout.Seed, mapType);
        new PortalPlacementService().Place(instance, layout, null);
        return instance;
    }

    private sealed class ArrivalWorld(StaticData data, IReadOnlyList<MapTemplate> templates) : IWorld
    {
        public ArrivalRegistry Registry { get; } = new();

        public StaticData Data { get; } = data;
        public GameConfiguration Configuration { get; } = new();
        public IReadOnlyList<MapTemplate> MapTemplates { get; } = templates;
        public IInstanceRegistry InstanceRegistry => Registry;

        /// <summary>The character's instance id only: the membership change is the instances' own cost, not an arrival handler's.</summary>
        public void TransferPlayer(IWorldConnection connection, IMapInstance targetInstance) =>
            ((CharacterEntity)connection.Character!).Data!.InstanceId = Registry.TextOf(targetInstance);

        public WorldId Id => throw new NotSupportedException();
        public string MinVersion => throw new NotSupportedException();
        public string CurrentVersion => throw new NotSupportedException();
        public GameTime Time => throw new NotSupportedException();
        public IPartyInstanceRegistry PartyInstances => throw new NotSupportedException();
        public IInstanceChoices InstanceChoices => throw new NotSupportedException();
        public void SpawnInInstance(IWorldConnection connection, IMapInstance instance) => throw new NotSupportedException();
        public Task DeSpawnPlayerAsync(IWorldConnection connection) => throw new NotSupportedException();
        public Task<bool> LeaveWorldAsync(IWorldConnection connection) => throw new NotSupportedException();
        public Task LoadAsync(CancellationToken token) => throw new NotSupportedException();
        public void Update(TimeSpan deltaTime) => throw new NotSupportedException();
    }

    /// <summary>The town and the forest a request is answered with, each already built: a request is a finished task, held.</summary>
    private sealed class ArrivalRegistry : IInstanceRegistry
    {
        public ArrivalInstance Town { get; set; } = null!;

        public ArrivalInstance Forest { get; set; } = null!;

        public Task<IMapInstance> GetOrCreateTownInstanceAsync(MapTemplateId templateId, ushort maxPlayers) => Town.Request;

        public Task<IMapInstance> GetOrCreateNormalInstanceAsync(uint characterId, MapTemplateId templateId) => Forest.Request;

        /// <summary>The instance's id as a character's row holds it.</summary>
        public string TextOf(IMapInstance instance) => ReferenceEquals(instance, Town.Instance) ? Town.IdText : Forest.IdText;

        public IMapInstance? GetInstanceById(Guid instanceId) =>
            instanceId == Town.Instance.InstanceId ? Town.Instance
            : instanceId == Forest.Instance.InstanceId ? Forest.Instance
            : null;

        public IReadOnlyCollection<IMapInstance> ActiveInstances => throw new NotSupportedException();
        public void RemoveInstance(Guid instanceId) => throw new NotSupportedException();
        public void ProcessExpiredInstances(TimeSpan abandonedInstanceLifetime) => throw new NotSupportedException();
    }

    /// <summary>
    /// A connection that counts what it is sent and runs a continuation where it is queued, when its task has
    /// finished: every task but one that a row hands it has. The one is a portal entry's request for an instance still
    /// being built, whose arrival it holds until <see cref="RunHeld" />. It allocates nothing of its own: the server's
    /// connection queues one object per continuation, of either kind. It holds what a select reads and writes on its
    /// connection.
    /// </summary>
    private sealed class ArrivalConnection(AccountId account) : IWorldConnection, ISettledContinuations
    {
        // The one continuation whose task had not ended when it was queued, until RunHeld runs it.
        private Task? _heldTask;
        private Delegate? _heldCallback;

        public int Sent { get; private set; }

        /// <summary>The pending spawn of the last select, kept when <see cref="EndSelect" /> clears the connection's.</summary>
        public PendingSpawn? LastPending { get; private set; }

        public ICharacter? Character { get; set; }
        public AccountId? AccountId { get; set; } = account;
        public GameplayWriteAuthority? GameplayAuthority { get; } = new(account, Guid.NewGuid(), 1);
        public PendingSpawn? PendingSpawn { get; private set; }
        public bool SelectInProgress { get; private set; }
        public long SelectStartedTicks { get; private set; }
        public bool RespawnInFlight { get; set; }
        public AccountLocale Locale { get; set; } = AccountLocale.enUS;
        public (ObjectGuid Npc, DialogueNodeId Node)? CurrentDialogue { get; set; }
        public ulong? CurrentTargetGuid { get; set; }

        public bool LeaveInProgress => false;
        public bool IsConnected => true;
        public bool IsClosing => false;
        public bool InGame => true;
        public bool InMap => true;
        public long Latency => 0;

        public void Send(OutboundPacket packet)
        {
            Sent++;
            packet.Release();
        }

        public void BeginSelect(long nowTicks)
        {
            SelectInProgress = true;
            SelectStartedTicks = nowTicks;
        }

        public void CancelSelect() => throw new InvalidOperationException("The select was cancelled.");

        public void SetPendingSpawn(ICharacter character, IMapInstance instance, long sinceTicks)
        {
            PendingSpawn = new PendingSpawn(character, instance, sinceTicks);
            SelectInProgress = false;
        }

        /// <summary>Forgets the finished select, so the next one finds a connection with no character and none pending.</summary>
        public void EndSelect()
        {
            LastPending = PendingSpawn ?? throw new InvalidOperationException("The select left no pending spawn.");
            PendingSpawn = null;
        }

        public void EnqueueContinuation<T>(Task<T> task, Action<T> callback)
        {
            if (!task.IsCompletedSuccessfully)
                throw new InvalidOperationException("A row handed the connection a task that has not finished.");

#pragma warning disable MA0045 // the task has completed successfully: reading its result does not block
            callback(task.Result);
#pragma warning restore MA0045
        }

        /// <summary>
        /// A continuation to run whatever its task's outcome, as a portal entry queues its arrival: run here when the
        /// task has ended, held for <see cref="RunHeld" /> when the instance asked for is still being built.
        /// </summary>
        public void EnqueueSettled<T>(Task<T> task, Action<Task<T>> callback)
        {
            if (task.IsCompleted)
            {
                callback(task);
                return;
            }

            if (_heldTask is not null)
                throw new InvalidOperationException("A row queued a second arrival before its first had run.");

            _heldTask = task;
            _heldCallback = callback;
        }

        /// <summary>
        /// The flush after a build was published: runs the held arrival, as the drain runs a continuation whose task
        /// has ended. Should the task not have ended yet, it waits, spinning: nothing but the task's own completion
        /// is waited for, so with the registry's task that is no wait at all.
        /// </summary>
        public void RunHeld()
        {
            if (_heldTask is not Task<IMapInstance> task || _heldCallback is not Action<Task<IMapInstance>> callback)
                throw new InvalidOperationException("The row queued no arrival to wait for.");

            _heldTask = null;
            _heldCallback = null;

            var spin = new SpinWait();
            while (!task.IsCompleted)
                spin.SpinOnce(sleep1Threshold: -1);

            callback(task);
        }

        public void EnqueueContinuation(Task task, Action callback)
        {
            if (!task.IsCompletedSuccessfully)
                throw new InvalidOperationException("A row handed the connection a task that has not finished.");

            callback();
        }

        public Guid Id => throw new NotSupportedException();
        public Task? ExecuteTask => throw new NotSupportedException();
        public string RemoteEndPoint => throw new NotSupportedException();
        public long RoundTripTime => throw new NotSupportedException();
        public long CurrentPacketArrivedTicks => throw new NotSupportedException();
        public uint LastInputSeq { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public AccountAccessLevel AccessLevel => throw new NotSupportedException();
        public bool LoadReportedEarly => throw new NotSupportedException();
        public void Close(bool expected = true) => throw new NotSupportedException();
        public Task CloseAsync(bool expected = true) => throw new NotSupportedException();
        public Task StartAsync(CancellationToken token = default) => throw new NotSupportedException();
        public void NoteLoadReportedEarly() => throw new NotSupportedException();
        public PendingSpawn? TakePendingSpawn() => throw new NotSupportedException();
        public void UpdateSession() => throw new NotSupportedException();
        public void UpdateMap() => throw new NotSupportedException();
        public void FlushContinuations() => throw new NotSupportedException();
    }

    private sealed class TownResolver(MapTemplateId town) : IRespawnTargetResolver
    {
        private readonly Task<MapTemplateId> _town = Task.FromResult(town);

        public Task<MapTemplateId> ResolveTownAsync(MapTemplateId from, CancellationToken ct) => _town;
    }

    /// <summary>Every save has already committed, and no character has one in flight.</summary>
    private sealed class FinishedSaver : ICharacterSaver
    {
        private static readonly Task<bool> s_committed = Task.FromResult(true);

        public Task<bool> Save(IWorldConnection connection, CharacterEntity character) => s_committed;

        public Task WhenIdle(CharacterId id) => Task.CompletedTask;

        public Task WhenAllIdle() => throw new NotSupportedException();

        public Task<bool> Save(IReadOnlyList<(IWorldConnection Connection, CharacterEntity Character)> characters) =>
            throw new NotSupportedException();

        public Task<bool> SaveOnDespawnAsync(CharacterEntity character, Func<Character, CancellationToken, Task>? prepareRow,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SaveOutcome> SaveTrade(
            IReadOnlyList<(IWorldConnection Connection, CharacterEntity Character, TradeSidePlan Side)> sides, TradeRecord record) =>
            throw new NotSupportedException();

        public Task<bool> TradeCommittedAsync(Guid tradeId, AccountId first, AccountId second) => throw new NotSupportedException();

        public void ReleaseHold(CharacterId id) => throw new NotSupportedException();
    }

    private sealed class NoOtherSessions : IWorldServer
    {
        public IReadOnlyList<IWorldConnection> SessionsOf(AccountId accountId, IWorldConnection except) => [];

        public IReadOnlyList<IWorldConnection> SessionsHoldingCharacter(CharacterId id, IWorldConnection except) => [];

        public ImmutableArray<IWorldConnection> Connections => throw new NotSupportedException();
        public IWorld World => throw new NotSupportedException();
        public Dictionary<NetworkPacketType, IWorldPacketHandler> PacketHandlers => throw new NotSupportedException();
        public PacketDispatchTelemetry PacketTelemetry => throw new NotSupportedException();
        public NetworkSendScheduler SendScheduler => throw new NotSupportedException();
        public bool PacketEncryption => throw new NotSupportedException();
        public ReceiveQueueLimit ReceiveQueueLimit => throw new NotSupportedException();
        public uint MeanTickMicros => throw new NotSupportedException();
    }

    private sealed class InlineWork : IWorldDatabaseWork
    {
        public Task<T> Run<T>(Func<Task<T>> operation) => operation();
    }

    /// <summary>The members of <see cref="IRepository{TEntity,TKey}" /> no arrival calls.</summary>
    private abstract class NoRepository<TEntity, TKey> : IRepository<TEntity, TKey> where TEntity : class, IDbEntity<TKey>
    {
        public virtual Task<TEntity?> FindByIdAsync(TKey id, bool track = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PagedResult<TEntity>> PaginateAsync(EntityPaginateFilter<TEntity> filter, bool track = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<TEntity>> FindAllAsync(bool track = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<List<TEntity>> FindByAsync(Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<List<TEntity>> CreateAsync(List<TEntity> entities, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(TKey id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>The one character, read and written back as a select does, each a finished task.</summary>
    private sealed class HeldCharacters(Character row) : NoRepository<Character, CharacterId>, ICharacterRepository
    {
        private readonly Task<Character?> _found = Task.FromResult<Character?>(row);
        private readonly Task<Character> _updated = Task.FromResult(row);

        public Task<Character?> FindForGameplayAsync(GameplayWriteAuthority authority, CharacterId id,
            CancellationToken cancellationToken = default) => _found;

        public Task<Character> UpdateForGameplayAsync(GameplayWriteAuthority authority, Character row,
            CancellationToken cancellationToken = default) => _updated;

        public Task<Character?> FindByNameAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CharacterRename> TryRenameAsync(CharacterId id, string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Character?> FindByIdAndAccountAsync(CharacterId id, AccountId accountId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<Character>> FindByAccountAsync(AccountId accountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CharacterCreationReply> CreateForGameplayAsync(GameplayWriteAuthority authority,
            CharacterCreationBatch batch, int maximum, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteForGameplayAsync(GameplayWriteAuthority authority, CharacterId id,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoSlots : ICharacterInventoryRepository
    {
        private static readonly Task<IReadOnlyCollection<CharacterInventory>> s_none =
            Task.FromResult<IReadOnlyCollection<CharacterInventory>>([]);

        public Task<IReadOnlyCollection<CharacterInventory>> GetByCharacterIdAsync(CharacterId characterId,
            CancellationToken cancellationToken = default) => s_none;

        public Task<CharacterInventory> CreateAsync(CharacterInventory inventory, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IList<CharacterInventory>> CreateAsync(IList<CharacterInventory> inventories,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CharacterInventory> UpdateAsync(CharacterInventory inventory, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoItems : NoRepository<ItemInstance, ItemInstanceId>, IItemInstanceRepository
    {
        private static readonly Task<IReadOnlyList<ItemInstance>> s_none = Task.FromResult<IReadOnlyList<ItemInstance>>([]);

        public Task<IReadOnlyList<ItemInstance>> GetByCharacterIdAsync(CharacterId characterId,
            CancellationToken cancellationToken = default) => s_none;
    }

    private sealed class NoAbilities : ICharacterAbilityRepository
    {
        private static readonly Task<IReadOnlyCollection<CharacterAbility>> s_none =
            Task.FromResult<IReadOnlyCollection<CharacterAbility>>([]);

        public Task<IReadOnlyCollection<CharacterAbility>> GetCharacterAbilitiesAsync(CharacterId characterId,
            CancellationToken cancellationToken = default) => s_none;

        public Task<CharacterAbility> CreateAsync(CharacterAbility ability, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IList<CharacterAbility>> CreateAsync(IList<CharacterAbility> abilities,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>The one account, for the select's locale read.</summary>
    private sealed class HeldAccounts(Account account) : NoRepository<Account, AccountId>, IAccountRepository
    {
        private readonly Task<Account?> _found = Task.FromResult<Account?>(account);

        public override Task<Account?> FindByIdAsync(AccountId id, bool track = false,
            CancellationToken cancellationToken = default) => _found;

        public Task<Account?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Account?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FailedLoginResult> RecordFailedLoginAsync(AccountId id, string attemptIp, DateTime now, DateTime? lockUntil,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> TryRecordApiLoginAsync(AccountId id, string lastIp, DateTime now,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

    }
}

/// <summary>
/// An instance as <see cref="ArrivalBenchmarks" /> hands it to a handler: itself, the finished task a request for it is
/// answered with, and its id as a character's row holds it, each made once so that a row allocates none of them.
/// </summary>
internal sealed class ArrivalInstance
{
    private readonly TaskCompletionSource<IMapInstance>? _build;

    /// <summary>An instance already built: a request for it is a finished task.</summary>
    public ArrivalInstance(MapInstance instance)
    {
        Instance = instance;
        Request = Task.FromResult<IMapInstance>(instance);
        IdText = instance.InstanceId.ToString();
    }

    /// <summary>An instance whose build is not published yet: a request for it ends with <see cref="Publish" />.</summary>
    public ArrivalInstance(MapInstance instance, TaskCompletionSource<IMapInstance> build)
    {
        Instance = instance;
        _build = build;
        Request = build.Task;
        IdText = instance.InstanceId.ToString();
    }

    public MapInstance Instance { get; }

    public Task<IMapInstance> Request { get; }

    public string IdText { get; }

    /// <summary>Finishes the request, as the registry does on the tick that publishes the build.</summary>
    public void Publish() => _build!.SetResult(Instance);
}

/// <summary>
/// A portal entry into an instance nobody has arrived in before (#883): <see cref="ArrivalBenchmarks" />' two portal
/// entries, each call into a new instance of the same layout. A character's own forest is a new instance on every
/// entry outside its re-entry window, so for a forest this is the usual arrival, not the exception.
/// </summary>
/// <remarks>
/// An instance is built with its layout's packet entries, off the tick, so a first arrival should cost what any other
/// does; when the entries were built by the first arrival, this row was that arrival and paid for them on the tick.
/// The instances of an iteration are built before it (<see cref="Instances" /> of them, the iteration's every call),
/// so a row is the arrival alone. They hold no creatures: an arrival reads the layout, the entry spawn and the portals.
/// <see cref="Portal_entry_into_a_forest_still_being_built" /> is the same arrival asked for before its instance's
/// build is published, as an entry that starts the build is.
/// </remarks>
[MemoryDiagnoser]
[InvocationCount(Instances, 16)]
public class FirstArrivalBenchmarks
{
    private const int Instances = 1024;

    private readonly ArrivalBenchmarks _arrivals = new();
    private readonly ArrivalInstance[] _fresh = new ArrivalInstance[Instances];
    private int _next;

    [GlobalSetup]
    public void Setup()
    {
        _arrivals.Setup();

        ArrivalInstance firstForest = _arrivals.NewForest();
        ArrivalInstance nextForest = _arrivals.NewForest();
        ArrivalInstance firstTown = _arrivals.NewTown();
        ArrivalInstance building = _arrivals.NewForestBeingBuilt();
        using MapInstance firstForestOwner = firstForest.Instance;
        using MapInstance nextForestOwner = nextForest.Instance;
        using MapInstance firstTownOwner = firstTown.Instance;
        using MapInstance buildingOwner = building.Instance;
        int before = _arrivals.EnterForest(firstForest);
        int forest = _arrivals.EnterForest(nextForest);
        int town = _arrivals.EnterTown(firstTown);
        int built = _arrivals.EnterForestBeingBuilt(building);
        if (forest - before != 2 || town - forest != 2 || built - town != 2)
            throw new InvalidOperationException("An arrival in a new instance did not send its transition and its layout.");
    }

    [GlobalCleanup]
    public void Cleanup() => _arrivals.Cleanup();

    [IterationCleanup]
    public void DisposeInstances()
    {
        foreach (ArrivalInstance instance in _fresh)
            instance.Instance.Dispose();
        Array.Clear(_fresh);
    }

    [IterationSetup(Target = nameof(First_portal_entry_into_a_forest))]
    public void NewForests()
    {
        for (int i = 0; i < _fresh.Length; i++)
            _fresh[i] = _arrivals.NewForest();
        _next = 0;
    }

    [IterationSetup(Target = nameof(First_portal_entry_into_the_town))]
    public void NewTowns()
    {
        for (int i = 0; i < _fresh.Length; i++)
            _fresh[i] = _arrivals.NewTown();
        _next = 0;
    }

    [IterationSetup(Target = nameof(Portal_entry_into_a_forest_still_being_built))]
    public void NewBuilds()
    {
        for (int i = 0; i < _fresh.Length; i++)
            _fresh[i] = _arrivals.NewForestBeingBuilt();
        _next = 0;
    }

    [Benchmark]
    public int First_portal_entry_into_a_forest() => _arrivals.EnterForest(_fresh[_next++]);

    /// <summary>
    /// The usual entry of a character's own forest: the request is handled while the instance is still being built,
    /// and the arrival runs once the build is published. It should cost what
    /// <see cref="First_portal_entry_into_a_forest" /> does: the arrival is queued to run whatever the build's outcome,
    /// and that queue entry is the connection's own. When the handler wrapped the request in a task that cannot fault
    /// instead, this row held the wrapper (160 B) and the wait for the thread pool to run it.
    /// </summary>
    [Benchmark]
    public int Portal_entry_into_a_forest_still_being_built() => _arrivals.EnterForestBeingBuilt(_fresh[_next++]);

    [Benchmark]
    public int First_portal_entry_into_the_town() => _arrivals.EnterTown(_fresh[_next++]);
}
