using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalon.Common.GameAuth;
using Avalon.Common.Telemetry;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Hosting.Telemetry;
using Avalon.Infrastructure;
using Avalon.Infrastructure.GameAuth;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.GameAuth;
using Avalon.World.Inventory;
using Avalon.World.Items;
using Avalon.World.Maintenance;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Presence;
using Avalon.World.Public;
using Avalon.World.Quests;
using Avalon.World.Reload;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abstractions;
using Avalon.World.Telemetry;
using Avalon.World.Threading;
using Avalon.World.Trading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Avalon.World;

public interface IWorldPacketHandler
{
    void Execute(IWorldConnection connection, Packet packet);
}

public abstract class WorldPacketHandler<TPacket> : IWorldPacketHandler where TPacket : Packet
{
    void IWorldPacketHandler.Execute(IWorldConnection connection, Packet packet) => Execute(connection, (TPacket)packet);

    public abstract void Execute(IWorldConnection connection, TPacket packet);
}

public class PacketHandlerAttribute : Attribute
{
    public PacketHandlerAttribute(NetworkPacketType packetType) => PacketType = packetType;

    public NetworkPacketType PacketType { get; }
}

public interface IWorldServer
{
    ImmutableArray<IWorldConnection> Connections { get; }

    /// <summary>
    /// Tick thread. Every connection of <paramref name="accountId" /> other than
    /// <paramref name="except" />: the connected ones, and the ones that have closed but whose
    /// despawn the tick has not started yet. A closed connection leaves <see cref="Connections" />
    /// before its despawn runs, so a lookup of <see cref="Connections" /> alone misses a character
    /// that is still live and has not queued its logout save.
    /// </summary>
    IReadOnlyList<IWorldConnection> SessionsOf(AccountId accountId, IWorldConnection except);
    IReadOnlyList<IWorldConnection> SessionsHoldingCharacter(CharacterId id, IWorldConnection except);

    IWorld World { get; }
    Dictionary<NetworkPacketType, IWorldPacketHandler> PacketHandlers { get; }

    /// <summary>The per-packet telemetry ProcessQueue records into, as CallListener does.</summary>
    PacketDispatchTelemetry PacketTelemetry { get; }

    /// <summary>The send threads every connection's packets leave on (#875).</summary>
    NetworkSendScheduler SendScheduler { get; }

    /// <summary>
    /// <c>Network:PacketEncryption</c> (#875), read once at startup: whether this world's connections seal every gameplay
    /// packet with the session layer inside TLS. A connection reads it when it is created and keeps it.
    /// </summary>
    bool PacketEncryption { get; }

    /// <summary>
    /// <c>Network:MaxReceiveQueueDepth</c> (#927), read once at startup: how many packets a connection may have waiting
    /// for the tick, and the count of the connections closed at it. A connection reads it when it is created.
    /// </summary>
    ReceiveQueueLimit ReceiveQueueLimit { get; }

    /// <summary>
    /// The world's mean tick time in microseconds (#1016), which every time-sync ping carries
    /// (<c>SPingPacket.TickMicros</c>): the mean busy time of the whole ticks (start to end, without the wait for the
    /// next) that ended in the last completed window of about one second. 0 until the first window completes. Written
    /// by the tick once per window; read on the tick, as the ping is queued.
    /// </summary>
    uint MeanTickMicros { get; }
}

public class WorldServer : ServerBase<WorldConnection>, IWorldServer
{
    public IGameAdmissionClient AdmissionClient => _services.GetRequiredService<IGameAdmissionClient>();
    public WorldTlsTransport TlsTransport => _services.GetRequiredService<WorldTlsTransport>();

    // Invoked on the tick so despawn removes the entity before any further world simulation. The despawn and its final
    // save first; then the session's end, which waits in its lane holding only what the end needs (#1051).
    internal Task DrainGameplayAsync(WorldConnection connection) => EndSessionAsync(DespawnForDrainAsync(connection));

    private async Task<SessionEnd?> DespawnForDrainAsync(WorldConnection connection)
    {
        await _world.DeSpawnPlayerAsync(connection).ConfigureAwait(false);
        try { await connection.WhenPendingOperationsIdle().ConfigureAwait(false); } catch (Exception) { }
        Task<WorldAdmissionResult>? pendingAdmission = connection.AdmissionWork;
        if (pendingAdmission is not null)
            try { await pendingAdmission.ConfigureAwait(false); } catch (Exception) { }
        GameSessionLease? lease = connection.GameSessionLease ?? (pendingAdmission is { IsCompletedSuccessfully: true } ? pendingAdmission.Result.Lease : null);
        // Why the session ended is read once, here, as the end is built; the lane only carries it (#967).
        return lease is null ? null : new SessionEnd(lease, connection.RenewalUnaccounted, connection.SessionEndReason);
    }

    // Bounded (#1051): a stop drains every connection at once, and identity must not receive every end together. This
    // method holds the end, never the connection, while it waits for its turn.
    private async Task EndSessionAsync(Task<SessionEnd?> despawned)
    {
        if (await despawned.ConfigureAwait(false) is { } end)
            await SessionEnds.Run(() => EndOrSkipAsync(end)).ConfigureAwait(false);
    }

    /// <summary>
    /// A session's end, at the front of its lane (#1051). Not sent once the stop's deadline has passed (the host's stop
    /// timeout), nor for a lease that ran out more than <see cref="SessionEndSkew" /> ago with no renewal outstanding:
    /// identity's session and the characters database's guard ran out with it, and an expired session holds nothing
    /// an end would release (a join, a reconnect, a takeover, a consolidation and a save all read its lease end). A
    /// session whose renewals the world cannot account for (<see cref="RenewalLedger" />) may be held by identity past
    /// this lease, so its end is always sent.
    /// </summary>
    private async Task<SessionLeaseResponse> EndOrSkipAsync(SessionEnd end)
    {
        CancellationToken stop = _sessionEndsStop;
        if (stop.IsCancellationRequested)
        {
            Interlocked.Increment(ref _sessionEndsDroppedAtStop);
            s_sessionEndsSkipped.Add(1, s_stoppedReason);
            return s_notSent;
        }

        if (!end.RenewalUnaccounted && end.Lease.HasRunOutBy(SessionEndSkew))
        {
            s_sessionEndsSkipped.Add(1, s_expiredReason);
            return s_notSent;
        }

        return await AdmissionClient.EndAsync(end.Lease, end.Reason, stop).ConfigureAwait(false);
    }

    /// <summary>
    /// What a session's end needs: the lease, whether a renewal asked for was never applied to it, and why it ended
    /// (a <c>GameSessionEndReasons</c> value, #967).
    /// </summary>
    private readonly record struct SessionEnd(GameSessionLease Lease, bool RenewalUnaccounted, string Reason);

    /// <summary>
    /// How long past its end a lease must be before its end is not sent (#1051): the clocks of the world, identity and
    /// the characters database may differ by this much, and identity's session may outlast the guard by that skew.
    /// </summary>
    public static readonly TimeSpan SessionEndSkew = TimeSpan.FromSeconds(5);

    private static readonly SessionLeaseResponse s_notSent = new() { Error = GameAuthErrors.ServiceUnavailable };
    private static readonly Counter<long> s_sessionEndsSkipped = DiagnosticsConfig.World.Meter.CreateCounter<long>(
        "world.session_ends.skipped", "{sessions}",
        "Game session ends not sent to identity: the lease had run out (expired) or the stop's deadline had passed (stopping)");
    private static readonly KeyValuePair<string, object?> s_expiredReason = new("reason", "expired");
    private static readonly KeyValuePair<string, object?> s_stoppedReason = new("reason", "stopping");

    // The stop's deadline for session ends (#1051): the host's stop token, set as the stop begins. None until then.
    private CancellationToken _sessionEndsStop;
    private int _sessionEndsDroppedAtStop;

    /// <summary>
    /// Where <see cref="DrainGameplayAsync" /> sends each session's end: <see cref="WorldDatabaseWork.SessionEnds" />, a
    /// bounded number at once (#1051). Settable for a test.
    /// </summary>
    public IWorldDatabaseWork SessionEnds { get; init; } = WorldDatabaseWork.SessionEnds;
    #region Scheduling (Move to HighRes Timer class)

    [SupportedOSPlatform("windows")]
    private static IntPtr CreateHighResTimer()
    {
        const uint TIMER_ALL_ACCESS = 0x1F0003;
        const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
        IntPtr h = CreateWaitableTimerExW(IntPtr.Zero, null,
            CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
        if (h == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "High-resolution waitable timer unavailable (requires Windows 10 1803+).");
        }

        return h;
    }

    [SupportedOSPlatform("windows")]
    private static void WaitHighRes(IntPtr timer, long qpcTicks)
    {
        // SetWaitableTimer's due time is in 100-ns units; negative = relative.
        long due = -(qpcTicks * 10_000_000 / Stopwatch.Frequency);
        SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false);
        WaitForSingleObject(timer, 0xFFFFFFFF);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWaitableTimerExW(
        IntPtr lpTimerAttributes, string? lpTimerName, uint dwFlags, uint dwDesiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetWaitableTimer(IntPtr hTimer, ref long pDueTime,
        int lPeriod, IntPtr pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine, bool fResume);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    #endregion
    private static readonly MethodInfo s_buildContextMethod =
        typeof(WorldServer).GetMethod(nameof(BuildContextFactory), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            $"Could not reflect {nameof(WorldServer)}.{nameof(BuildContextFactory)}. " +
            "Ensure the method is non-public, static, and not overloaded.");

    // Tick scheduling
    private static readonly long s_ticksPerFrame = Stopwatch.Frequency / WorldTicks.PerSecond; // 60Hz
    private static readonly long s_spinThresholdTicks = Stopwatch.Frequency / 1000; // 1ms

    private Thread? _tickThread;
    private int? _tickThreadCpu;
    private volatile bool _tickRunning;
    private IntPtr _waitableTimer; // Windows high-res timer handle
    private readonly TaskCompletionSource _tickExited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly IReplicatedCache _cache;
    private readonly IServiceProvider _services;
    private readonly ConcurrentDictionary<Type, Func<IConnection, Packet?, object>>
        _contextFactoryCache = new();
    private readonly Stopwatch _gameTime = new();
    private readonly ILogger<WorldServer> _logger;
    private readonly PartyService _parties;
    private readonly TradeService? _trades;
    private readonly ThrottledErrorLog _memberStatusErrors;
    private readonly QuestService? _quests;
    private readonly ThrottledErrorLog _questFlushErrors;

    // The inventory flush's modifier entries (#1179): taken by each connection's update and given back once it is
    // encoded. The flush runs on the tick, after both passes, one connection at a time.
    private readonly ItemModifierDtoPool _inventoryModifierDtos = new();

    // The Awakening stage (#1180): settles, wakes worn sealed affixes and sends their progress, before the quest and
    // inventory flushes, so a wake's item reaches the client in the same tick's inventory update. It visits only the
    // characters the queue took, so an idle tick costs nothing per connection.
    private readonly AwakeningQueue _awakeningQueue;
    private readonly AwakeningFlusher _awakening;
    private readonly ThrottledErrorLog _awakeningFlushErrors;
    private readonly ThrottledErrorLog _questResetErrors;
    private readonly IScriptHotReloader _scriptHotReloader;
    private readonly IScriptManager _scriptManager;
    private readonly Stopwatch _serverTimer = new();
    private readonly IWorld _world;
    private readonly ICharacterSaver _characterSaver;
    private readonly IWorldEntryGate? _entryGate;
    private readonly WorldMaintenanceCoordinator? _maintenanceCoordinator;
    private readonly TickThreadGuard? _tickThreadGuard;
    private readonly PresenceCapture? _presence;
    private readonly ThrottledErrorLog _presenceErrors;
    private readonly ThrottledErrorLog _maintenanceErrors;
    // The container's clock, so the maintenance cutoff and the entry checks read one time.
    private readonly TimeProvider _time;

    // The save margin in the clock's timestamps, for the long-tick guard (#977): one subtraction a tick. A clock that
    // counts no time (a test's substitute, whose frequency is 0) never trips it.
    private readonly long _saveMarginTimestamps;
    private readonly NetworkSendScheduler _sendScheduler;
    private readonly ReceiveQueueLimit _receiveQueueLimit;
    private readonly ConcurrentQueue<WorldConnection> _pendingDisconnects = new();

    /// <summary>
    /// The time-sync ping's period in ticks: 60 Hz × 5 s (#1034, 600 before). Spread across ticks so all connections do
    /// not ping on the same tick: each connection pings on its own slot (<see cref="WorldConnection.TimeSyncSlot" />).
    /// The liveness check rides it.
    /// </summary>
    public const int TimeSyncTicksPeriod = 300;

    // Monotonic tick counter used solely to derive the time-sync ping phase.
    private long _pingTickCounter;

    // The ticks in windows of about a second (#1016): the mean tick time the pings carry, and world.tick.rate. Written
    // on the tick only; the rate gauge reads the last closed window's figure from the meter's thread.
    private TickWindow _tickWindow = new(Stopwatch.Frequency);

    // The world's tick count (#1066): the send scheduler's, which every ping reads, and the container's, which every
    // instance's broadcast reads. Advanced as each tick ends.
    private readonly WorldTicks _ticks;
    private volatile bool _isListening;

    public bool IsListening => _isListening;

    /// <summary>
    /// The ticks completed (<see cref="WorldTicks.Completed" />): the number every state broadcast and time-sync ping
    /// carries (#1066), and the ready heartbeat's sign that the tick moves. A tick that throws is not counted.
    /// </summary>
    public long CompletedTicks => _ticks.Completed;

    /// <inheritdoc />
    public uint MeanTickMicros => _tickWindow.MeanMicros;

    private Histogram<double> _tickDuration;
    private Histogram<double> _deadlineOvershoot;
    private Histogram<double> _worldUpdateDuration;
    private Histogram<double> _sessionUpdateDuration;
    private Histogram<double> _postUpdateDuration;

    // The stages after the world update (#875), one tag each, cached so timing a stage allocates nothing.
    private static readonly KeyValuePair<string, object?> s_awakeningStage = new("stage", "awakening");
    private static readonly KeyValuePair<string, object?> s_questsStage = new("stage", "quests");
    private static readonly KeyValuePair<string, object?> s_inventoryStage = new("stage", "inventory");
    private static readonly KeyValuePair<string, object?> s_sheetStage = new("stage", "sheet");
    private static readonly KeyValuePair<string, object?> s_abilityAmountsStage = new("stage", "ability_amounts");
    private static readonly KeyValuePair<string, object?> s_partyStatusStage = new("stage", "party_status");
    private static readonly KeyValuePair<string, object?> s_presenceStage = new("stage", "presence");
    private static readonly KeyValuePair<string, object?> s_pingsStage = new("stage", "pings");
    private static readonly KeyValuePair<string, object?> s_outboxStage = new("stage", "outbox");
    private static readonly KeyValuePair<string, object?> s_continuationsStage = new("stage", "continuations");
    private readonly TickHistogram _tickDurationHist = new();
    private readonly TickHistogram _deadlineOvershootHist = new();
    private readonly TickHistogram _worldUpdateHist = new();
    private readonly TickHistogram _sessionUpdateHist = new();

    public WorldServer(IPacketManager packetManager,
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider,
        IOptions<HostingConfiguration> hostingOptions,
        IWorld world,
        IScriptManager scriptManager,
        IReplicatedCache cache,
        IScriptHotReloader scriptHotReloader,
        ICharacterSaver characterSaver,
        PartyService parties,
        IWorldEntryGate? entryGate = null,
        WorldMaintenanceCoordinator? maintenanceCoordinator = null,
        TradeService? trades = null) : base(packetManager, loggerFactory.CreateLogger<WorldServer>(),
        serviceProvider,
        hostingOptions,
        PacketDispatchTelemetry.From(DiagnosticsConfig.World.Source, DiagnosticsConfig.World.Meter,
            hostingOptions.Value.Telemetry))
    {
        _scriptManager = scriptManager;
        _cache = cache;
        _services = serviceProvider;
        _scriptHotReloader = scriptHotReloader;
        _characterSaver = characterSaver;
        _entryGate = entryGate;
        _maintenanceCoordinator = maintenanceCoordinator;
        _maintenanceCoordinator?.SetDrainObserver(() => _pendingDisconnects.IsEmpty);
        _parties = parties;
        _trades = trades;
        _logger = loggerFactory.CreateLogger<WorldServer>();
        _memberStatusErrors = new ThrottledErrorLog(_logger,
            serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System, "The party member status flush");
        // #433. Optional here, like the clock above, so the tests that build a WorldServer from a bare provider still do.
        _quests = serviceProvider.GetService<QuestService>();
        _questFlushErrors = new ThrottledErrorLog(_logger,
            serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System, "The quest flush");
        _questResetErrors = new ThrottledErrorLog(_logger,
            serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System, "The quest reset check");
        _awakeningQueue = serviceProvider.GetRequiredService<AwakeningQueue>();
        _awakening = new AwakeningFlusher(serviceProvider.GetRequiredService<ICharacterEconomy>(), _logger);
        _awakeningFlushErrors = new ThrottledErrorLog(_logger,
            serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System, "The awakening flush");
        _world = world;
        // Optional, like the clock: the tests that build a WorldServer from a bare provider have none.
        _tickThreadGuard = serviceProvider.GetService<TickThreadGuard>();
        _presence = serviceProvider.GetService<PresenceCapture>();
        _presenceErrors = new ThrottledErrorLog(_logger,
            serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System, "The presence capture");
        _time = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        long frequency = _time.TimestampFrequency;
        _saveMarginTimestamps = frequency > 0
            ? (long)Math.Ceiling(GameAuthPolicy.SaveDrainMargin.Ticks * (double)frequency / TimeSpan.TicksPerSecond)
            : long.MaxValue;
        // #875. From the container; the tests that build a WorldServer from a bare provider get one with the defaults.
        _sendScheduler = serviceProvider.GetService<NetworkSendScheduler>()
            ?? new NetworkSendScheduler(new NetworkConfiguration(), loggerFactory, _time, NetworkSendMetrics.Disabled);
        _ticks = _sendScheduler.Ticks;
        // #927. From the container, as the scheduler is; a bare provider gets the default depth.
        _receiveQueueLimit = serviceProvider.GetService<ReceiveQueueLimit>() ?? ReceiveQueueLimit.Default;
        _maintenanceErrors = new ThrottledErrorLog(_logger,
            serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System, "The maintenance countdown");

        _logger.LogInformation("R2R enabled: {R2R}",
            System.Runtime.CompilerServices.RuntimeFeature.IsSupported("IsDynamicCodeCompiled"));

        PacketHandlers = new Dictionary<NetworkPacketType, IWorldPacketHandler>();

        Dictionary<NetworkPacketType, Type> packetHandlers = GamePacketHandlerLayer.Discover();

        foreach ((NetworkPacketType packetType, Type handlerType) in packetHandlers)
        {
            // If the handler constructor requires IWorldServer, pass 'this' explicitly to
            // avoid a circular DI resolution (WorldServer is still being constructed here).
            bool needsWorldServer = handlerType.GetConstructors()
                .Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IWorldServer)));
            object handler = needsWorldServer
                ? ActivatorUtilities.CreateInstance(serviceProvider, handlerType, (IWorldServer)this)
                : ActivatorUtilities.CreateInstance(serviceProvider, handlerType);
            PacketHandlers.Add(packetType, (IWorldPacketHandler)handler);
            _logger.LogInformation("Registered packet handler {HandlerType} for packet type {PacketType}", handlerType,
                packetType);
        }

        // The meter holds the gauge; nothing here needs it again.
        DiagnosticsConfig.World.Meter.CreateObservableGauge(
            "world.tick.rate", () => _tickWindow.TicksPerSecond, "tps", "World tick rate per second");

        Meter meter = DiagnosticsConfig.World.Meter;
        _tickDuration = WorldHistograms.Microseconds(meter, "world.tick.duration",
            "Duration of a world tick in microseconds");
        _deadlineOvershoot = WorldHistograms.Microseconds(meter, "world.tick.deadline_overshoot",
            "How much the tick loop overshot its deadline (positive) or woke early (negative), in microseconds",
            WorldHistograms.OvershootMicroseconds);
        _worldUpdateDuration = WorldHistograms.Microseconds(meter, "world.update.duration",
            "Duration of the world update phase of the tick loop in microseconds");
        _sessionUpdateDuration = WorldHistograms.Microseconds(meter, "world.session_update.duration",
            "Duration of the session update phase of the tick loop in microseconds");
        _postUpdateDuration = WorldHistograms.Microseconds(meter, "world.post_update.duration",
            "Duration of one stage after the world update (flushers, pings, the send threads' wake-up, continuations) in microseconds, by stage",
            WorldHistograms.StageMicroseconds);

        WorldGauges.Register(DiagnosticsConfig.World.Meter, () => Connections, () => _world.InstanceRegistry);
    }

    public new ImmutableArray<IWorldConnection> Connections =>
        TypedConnections.CastArray<IWorldConnection>();

    protected override PacketTags DescribeConnection(IConnection connection) =>
        connection is WorldConnection world ? world.TelemetryTags() : base.DescribeConnection(connection);

    public IWorld World => _world;

    public NetworkSendScheduler SendScheduler => _sendScheduler;

    public bool PacketEncryption => _sendScheduler.PacketEncryption;

    public ReceiveQueueLimit ReceiveQueueLimit => _receiveQueueLimit;

    public IReadOnlyList<IWorldConnection> SessionsOf(AccountId accountId, IWorldConnection except)
    {
        // Connections first, then the queue: a close enqueues its despawn before it leaves the
        // list, so a connection missing from the first snapshot is already in the second.
        List<IWorldConnection> sessions = [];
        foreach (IWorldConnection connection in Connections)
        {
            if (!ReferenceEquals(connection, except) && connection.AccountId == accountId)
                sessions.Add(connection);
        }

        foreach (WorldConnection closed in _pendingDisconnects)
        {
            if (!ReferenceEquals(closed, except) && closed.AccountId == accountId && !sessions.Contains(closed))
                sessions.Add(closed);
        }

        return sessions;
    }

    public IReadOnlyList<IWorldConnection> SessionsHoldingCharacter(CharacterId id, IWorldConnection except)
    {
        return Connections.Concat(_pendingDisconnects).Distinct(ReferenceEqualityComparer.Instance).Cast<IWorldConnection>()
            .Where(c => !ReferenceEquals(c, except) &&
                (c.Character ?? c.PendingSpawn?.Character) is Entities.CharacterEntity { Data: { } row } && row.Id == id).ToArray();
    }

    public Dictionary<NetworkPacketType, IWorldPacketHandler> PacketHandlers { get; }

    /// <summary>The port opens at the end of the load in <see cref="ExecuteAsync" />, not at host start (#665).</summary>
    protected override bool ListenOnStart => false;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Validate certificate loading and workload configuration before the port becomes ready.
        WorldTlsTransport transport = TlsTransport;
        IGameAdmissionClient admission = AdmissionClient;
        await Task.Run(() => _scriptManager.Load(), stoppingToken);

        // The names this world accepts, for the admin app. Required, as the reload handler below is: a host that
        // builds a WorldServer registers the publisher, so a missing one fails startup rather than going unnoticed.
        ScriptCatalogPublisher catalog = _services.GetRequiredService<ScriptCatalogPublisher>();
        await catalog.PublishAsync();

        await _world.LoadAsync(stoppingToken);

        if (_maintenanceCoordinator is not null)
            await _maintenanceCoordinator.InitializeAsync(stoppingToken);

        // Hot-reloaded scripts can bring names the load did not see. The event runs on the compiler's thread; the
        // publish never throws, so nothing is lost by not awaiting it.
        PublishCatalogOnHotReload(catalog);
        _scriptHotReloader.Start();

        await CacheSubscribeAsync();

        RegisterNewConnectionListener(NewConnection);

        _serverTimer.Start();

        _gameTime.Start();

        if (OperatingSystem.IsWindows())
            _waitableTimer = CreateHighResTimer();

        // The tick-thread assertion (#639) checks only when turned on: Game:TickThreadGuard, for development.
        if (_world.Configuration.TickThreadGuard)
        {
            TickThreadGuard.Enable();
            _logger.LogInformation("The tick-thread assertion is on (Game:TickThreadGuard)");
        }

        // Game:TickThreadCpu (#1002), applied by the tick thread itself before its first tick.
        _tickThreadCpu = _world.Configuration.TickThreadCpu;

        // The send threads before the tick that wakes them (#875).
        _sendScheduler.Start();
        _logger.LogInformation("Packet encryption: {Mode}",
            _sendScheduler.PacketEncryption ? "session layer inside TLS" : "TLS alone");

        _tickRunning = true;
        _tickThread = new Thread(TickLoop)
        {
            Name = "WorldServer.Tick",
            IsBackground = false,
            Priority = ThreadPriority.Highest,
        };
        _tickThread.Start();

        // Only now does the port open (#665): scripts, the world, the cache subscription, the
        // connection listener and the tick loop are all ready. A load that threw above never gets
        // here, so a failed start never leaves an endpoint accepting clients; a stop that began
        // during the load makes this a no-op. A port that cannot be bound fails the start, and the
        // tick it would have fed stops with it.
        try
        {
            StartListening();
            _isListening = true;
        }
        catch
        {
            _tickRunning = false;
            throw;
        }

        // When the host signals shutdown, stop the tick loop.
        // 1 frame (~16ms) of shutdown latency is acceptable and avoids
        // a second sync primitive to wake the timer early.
        await using CancellationTokenRegistration _ = stoppingToken.Register(static state =>
            ((WorldServer)state!)._tickRunning = false, this);

        await _tickExited.Task;

        _logger.LogInformation("World server stopped... Ran for {Minutes}mins",
            (int)_serverTimer.Elapsed.TotalMinutes);
    }

    protected override async Task OnStoppingAsync(CancellationToken stoppingToken)
    {
        _isListening = false;

        // Session ends still waiting for their turn when the host's stop timeout passes are not sent, and one in flight
        // is cancelled (#1051): a hung identity must not hold the stop past the pod's grace period. The saves come
        // before the ends and are waited for as below; a session not ended runs out with its lease.
        _sessionEndsStop = stoppingToken;

        // The restart drain (#768) first, with the listener already stopped: warn the players, and wait until they
        // have left or the drain's deadline has passed. Its countdown and cutoff run on the tick (#639), so the tick
        // keeps running until it ends. Only a running tick can end it; a stop during the load has none, and skips it.
        if (_maintenanceCoordinator is not null && _tickRunning && _tickThread is { IsAlive: true })
            await _maintenanceCoordinator.DrainForRestartAsync(stoppingToken).ConfigureAwait(false);

        // The tick goes first. It no longer writes to sockets (#875), but a despawn it would start must not race the
        // shutdown's own pass below.
        _tickRunning = false;
        if (_tickThread is not null && _tickThread.IsAlive)
            _tickThread.Join(TimeSpan.FromSeconds(5));

        // Awaited, and all at once: the shutdown notice is delivered by the close, so returning
        // before they finish lets the host exit with the packets still queued.
        var closing = new List<Task>();
        foreach (IWorldConnection connection in Connections)
            closing.Add(GracefulShutdownHelper.NotifyAndCloseAsync(connection, "Server is shutting down", DisconnectReason.ServerShutdown, _logger));

        await Task.WhenAll(closing).ConfigureAwait(false);

        // Closing a connection enqueues its despawn, and the tick that would normally dequeue it
        // has stopped. That despawn is what writes the character back — without this pass every
        // logged-in character is left online in the database with a stale position. One pass, on
        // this thread, with the tick joined: nothing else is touching the instances. It is awaited
        // rather than dropped because the process is about to exit, and it is not given a timeout
        // of its own for the same reason the close handler has none — the bound is the database's,
        // and cutting it short would discard the save this exists to make. Unless a trade is still
        // unsettled (#1175): the despawn saves its hold keeps waiting never finish, so then the wait
        // is bounded by the save window below.
        var despawning = new List<Task>();
        while (_pendingDisconnects.TryDequeue(out WorldConnection? disconnected))
        {
            // Each on its own: one that throws (the tick-thread assertion, say, when the tick outlived its join
            // above) must not cost the other characters their despawn saves, or skip the wait for saves below.
            try
            {
                despawning.Add(disconnected.DrainGameplayAsync());
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to despawn account {AccountId} at shutdown", disconnected.AccountId?.Value);
            }
        }

        // The save window (#1175): SaveDrainLimit from here, once every despawn has started and so queued its save.
        // Completing trades settle inside it, alongside the saves already writing rather than ahead of them: a trader's
        // despawn save waits on its trade's hold, and is snapshotted at the release, once the outcome is applied; every
        // other character's is already in its chain. Nothing settling touches is touched by the despawns from here on:
        // what remains of each is awaiting its save and ending its session.
        long saveWindowStart = Stopwatch.GetTimestamp();
        var despawned = Task.WhenAll(despawning);
        bool tradesSettled = await SettleTradesAsync(SaveDrainLimit, stoppingToken).ConfigureAwait(false);

        try
        {
            if (tradesSettled)
                await despawned.ConfigureAwait(false);
            else
                await despawned.WaitAsync(SaveWindowLeft(saveWindowStart), stoppingToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!tradesSettled && e is TimeoutException or OperationCanceledException)
        {
            _logger.LogError(
                "Shutdown stopped waiting for the despawns at the end of its {Limit} save window or at the host's stop " +
                "timeout: a trade still unsettled holds its characters' despawn saves",
                SaveDrainLimit);
        }
        catch (Exception e)
        {
            // A despawn's task does not fault today; if one ever does, the wait for saves below still runs.
            _logger.LogError(e, "A despawn at shutdown failed");
        }

        int dropped = Interlocked.Exchange(ref _sessionEndsDroppedAtStop, 0);
        if (dropped > 0)
        {
            _logger.LogWarning(
                "The stop's deadline passed before {Count} game sessions were ended with identity; each runs out with its lease",
                dropped);
        }

        // The pass above only covers despawns it started. A tick starts each despawn without
        // waiting for it, so one begun on an earlier tick can still be queued behind another save,
        // or be mid-transaction, and the host disposes the database as soon as this returns. The
        // periodic saves in flight are the same. Bounded by the host's own stop timeout and by
        // SaveDrainLimit, because a write that never returns must not hold the process up forever; with a
        // trade unsettled, by what is left of the save window, since its holds never drain.
        await WaitForSavesAsync(tradesSettled ? SaveDrainLimit : SaveWindowLeft(saveWindowStart), stoppingToken)
            .ConfigureAwait(false);

        // Last (#875): the send threads outlived the tick and the closes, so each close's notice went out (or its budget
        // ran out) with its owner still running, and no close is left waiting on a stopped owner. A send after the closes,
        // a despawn's among them, is refused: the connection is already closed. Background threads: one that does not
        // stop in time does not hold the process up.
        if (!_sendScheduler.Stop(SendThreadsStopLimit))
            _logger.LogWarning("A send thread did not stop within {Limit}", SendThreadsStopLimit);

        if (_waitableTimer != IntPtr.Zero)
        {
            CloseHandle(_waitableTimer);
            _waitableTimer = IntPtr.Zero;
        }
    }

    /// <summary>How long shutdown waits for character saves still in flight before giving up on them.</summary>
    public TimeSpan SaveDrainLimit { get; init; } = DefaultSaveDrainLimit;

    /// <summary>The production <see cref="SaveDrainLimit" />; <c>World:Shutdown:SaveMargin</c> must cover it.</summary>
    public static readonly TimeSpan DefaultSaveDrainLimit = TimeSpan.FromSeconds(20);

    /// <summary>How long the shutdown waits, in all, for the send threads to stop once everything else has (#875).</summary>
    public static readonly TimeSpan SendThreadsStopLimit = TimeSpan.FromSeconds(2);

    /// <summary>What is left of the shutdown's save window, which began at <paramref name="start" />; never negative.</summary>
    private TimeSpan SaveWindowLeft(long start)
    {
        TimeSpan left = SaveDrainLimit - Stopwatch.GetElapsedTime(start);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>
    /// Completing trades settle once the tick has stopped (#1175), after the closes and once every despawn has started,
    /// so no other character's close or save waits behind them: each waits for its paired save and record lookups,
    /// within <paramref name="limit" /> and the host's stop timeout, so a despawn save the trade holds is written with
    /// the outcome applied. A trade still unsettled keeps its holds, and the shutdown's later waits stop at the end of
    /// the save window. Only once the tick thread has exited, and so unbound its guard: one that outlived its join
    /// would still be applying completions itself.
    /// </summary>
    /// <returns>Whether every completing trade settled, or there was none to settle.</returns>
    private async Task<bool> SettleTradesAsync(TimeSpan limit, CancellationToken stoppingToken)
    {
        if (_trades is null)
            return true;

        if (_tickThread is { IsAlive: true })
        {
            _logger.LogError("The tick did not stop in time; any completing trade is left unsettled and keeps its holds");
            return false;
        }

        try
        {
            return await _trades.SettleAsync(limit, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Settling the completing trades at shutdown failed");
            return false;
        }
    }

    private async Task WaitForSavesAsync(TimeSpan limit, CancellationToken stoppingToken)
    {
        Task saves = _characterSaver.WhenAllIdle();
        if (saves.IsCompleted)
            return;

        try
        {
            await saves.WaitAsync(limit, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException)
        {
            _logger.LogError(
                "Shutdown stopped waiting for character saves still in flight at the end of its {Window} save window or " +
                "at the host's stop timeout; changes since those characters' last committed save may be lost",
                SaveDrainLimit);
        }
    }

    private void TickLoop()
    {
        // On its own CPU, with every other thread of the world kept off that core (#1002); unset, nothing happens. Once,
        // before the first tick.
        TickThreadPlacement.PinCurrentThread(_tickThreadCpu, LinuxCpuAffinity.Instance, _logger);

        // The tick's sends wake their send threads once, at the tick's end (the outbox stage), not once per packet (#875).
        NetworkSendScheduler.DeferSignalsOnCurrentThread();
        // From here until the loop ends, World-side state that only the tick may change refuses any other thread
        // (while the guard is enabled, #639). Unbound in the finally, so the shutdown despawn that runs once the tick is joined passes.
        _tickThreadGuard?.Bind();
        long next = Stopwatch.GetTimestamp() + s_ticksPerFrame;
        TimeSpan prev = _gameTime.Elapsed;

        try
        {
            while (_tickRunning)
            {
                try
                {
                    TimeSpan now = _gameTime.Elapsed;
                    TimeSpan deltaTime = now - prev;
                    if (deltaTime.TotalMilliseconds < 1)
                        deltaTime = TimeSpan.FromMilliseconds(1);
                    prev = now;

                    long tickStart = Stopwatch.GetTimestamp();
                    long overshootTicks = tickStart - (next - s_ticksPerFrame);
                    double overshootUs = TicksToUs(overshootTicks);
                    _deadlineOvershootHist.Record((long)overshootUs);
                    _deadlineOvershoot.Record(overshootUs);

                    // The tick count moves at the end of Update (UpdateCore); the tick rate (world.tick.rate) comes from the
                    // tick's own window there too, with no clock read.
                    Update(deltaTime, tickStart);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Tick failed");
                }

                // Sleep until ~1ms before deadline...
                long remaining = next - Stopwatch.GetTimestamp();
                if (remaining > s_spinThresholdTicks)
                {
                    long sleepTicks = remaining - s_spinThresholdTicks;
                    if (OperatingSystem.IsWindows() && _waitableTimer != IntPtr.Zero)
                    {
                        WaitHighRes(_waitableTimer, sleepTicks);
                    }
                    else
                    {
                        // Dedicated tick thread loop: Task.Delay would hop threads and add jitter
                        // well above the sub-millisecond budget of the 60Hz tick deadline.
#pragma warning disable MA0045
                        Thread.Sleep(TimeSpan.FromMilliseconds(
                            sleepTicks * 1000.0 / Stopwatch.Frequency));
                    }
#pragma warning restore MA0045
                }

                // ...then spin the final ~1ms for precision.
                while (Stopwatch.GetTimestamp() < next)
                    Thread.SpinWait(64);

                // Deadline-based, not delta-based: prevents drift.
                next += s_ticksPerFrame;

                // Spiral-of-death guard: if we fell >4 frames behind
                // (GC pause, huge packet burst), resync instead of catching up.
                long lag = Stopwatch.GetTimestamp() - next;
                if (lag > s_ticksPerFrame * 4)
                {
                    _logger.LogWarning("Tick loop fell {LagMs}ms behind; resyncing",
                        lag * 1000 / Stopwatch.Frequency);
                    next = Stopwatch.GetTimestamp() + s_ticksPerFrame;
                }
            }
        }
        finally
        {
            _tickThreadGuard?.Unbind();
            _tickExited.TrySetResult();
        }
    }

    // protected so a test can drive one tick without a socket loop behind it.
    protected void Update(TimeSpan elapsedTime, long tickStart)
    {
        if (!WorldTickTrace.Log.IsEnabled())
        {
            UpdateCore(elapsedTime, tickStart);
            return;
        }

        long previous = WorldTickTrace.TickId;
        WorldTickTrace.TickId = tickStart;
        long sessionPackets = WorldTickTrace.SessionPackets;
        bool completed = false;
        try
        {
            UpdateCore(elapsedTime, tickStart);
            completed = true;
        }
        finally
        {
            WorldTickTrace.TickId = previous;
            WorldTickTrace.Log.WorldTick(tickStart, Stopwatch.GetTimestamp() - tickStart,
                Stopwatch.Frequency, completed ? 1 : 0, WorldTickTrace.SessionPackets - sessionPackets);
        }
    }

    private void UpdateCore(TimeSpan elapsedTime, long tickStart)
    {
        long t0 = Stopwatch.GetTimestamp();
        // Process disconnects on the tick thread to avoid racing with MapInstance.Update
        while (_pendingDisconnects.TryDequeue(out WorldConnection? disconnected))
        {
            _ = disconnected.DrainGameplayAsync();
        }

        // Cache once — both phases iterate the same set: one snapshot of the typed list, and the interface view of the
        // same array (an upcast, which holds whatever the list is built from).
        ImmutableArray<WorldConnection> typedConns = TypedConnections;
        var conns = ImmutableArray<IWorldConnection>.CastUp(typedConns);

        // The tick's time (#977): one read of the container's clock for every connection's lease check, and through it
        // every dispatch's authorization this tick, where each read the clock for itself.
        var now = TickTime.Read(_time);

        // Revoked or expiring sessions leave their instance before packets and simulation run.
        foreach (IWorldConnection connection in conns)
            if (connection is WorldConnection admitted) admitted.AdvanceGameplayLease(now);

        // Before the session pass, so a cutoff blocks a non-Admin's queued packets on the tick it arrives. Applies a
        // state the notification or the reconciliation offered from off the tick. Contained: the tick goes on.
        try
        {
            _maintenanceCoordinator?.Advance(now.UtcNow, conns);
        }
        catch (Exception e)
        {
            _maintenanceErrors.Failed(e);
        }

        foreach (IWorldConnection worldConnection in conns)
            worldConnection.UpdateSession();

        // The long-tick guard (#977): the passes judge every lease at the tick's time, so a session pass that ran past
        // the save margin since then reads the time again and checks every lease at it before the map pass dispatches.
        // A lease that came due meanwhile ends here, as it would have at the next packet when each read the clock.
        if (_time.GetTimestamp() - now.Timestamp >= _saveMarginTimestamps)
        {
            now = TickTime.Read(_time);
            foreach (IWorldConnection connection in conns)
                if (connection is WorldConnection admitted) admitted.AdvanceGameplayLease(now);
        }

        long t1 = Stopwatch.GetTimestamp();
        double sessionUs = TicksToUs(t1 - t0);
        _sessionUpdateHist.Record((long)sessionUs);
        _sessionUpdateDuration.Record(sessionUs);

        // After the session pass, so a load report that arrived this tick releases its own barrier
        // rather than being beaten to it, and before the world update, so a character released here
        // is simulated on the tick that released it.
        long barrierNowTicks = _time.GetUtcNow().UtcTicks;
        var barrierTimeout = TimeSpan.FromSeconds(_world.Configuration.CharacterLoadTimeoutSeconds);

        CharacterReadinessBarrier.ReleaseExpired(conns.AsSpan(), _world, barrierNowTicks, barrierTimeout, _logger,
            _entryGate, _maintenanceCoordinator, _time);

        // The other half of the same failure: a select that never reached a pending spawn at all,
        // so ReleaseExpired cannot see it. Sharing the timeout because both are "the select
        // pipeline stopped making progress"; split them if they ever need different windows.
        CharacterReadinessBarrier.CancelExpiredSelects(conns, barrierNowTicks, barrierTimeout, _logger);

        _world.Update(elapsedTime);
        long t2 = Stopwatch.GetTimestamp();
        double worldUs = TicksToUs(t2 - t1);
        _worldUpdateHist.Record((long)worldUs);
        _worldUpdateDuration.Record(worldUs);

        // Each stage below is timed into world.post_update.duration by its stage tag (#875): StageDone records the
        // time since the previous stage ended and returns now, the next stage's start.
        long stageStart = t2;

        // Awakening (#1180): each character a kill or a moved bar made due settles its points and wakes the worn sealed
        // affixes whose bars are full, then is told the progress that moved. Before the quest and inventory flushes, so
        // a woken item, now attuned, leaves in this tick's inventory update with its new stats behind it. Only the
        // characters queued since the last tick are visited. Contained per character: one character's throw costs nobody
        // else.
        StaticData data = _world.Data;
        ReadOnlySpan<CharacterEntity> due = _awakeningQueue.Take();
        for (int i = 0; i < due.Length; i++)
        {
            try
            {
                _awakening.Flush(due[i], data);
            }
            catch (Exception e)
            {
                _awakeningFlushErrors.Failed(e);
            }
        }

        stageStart = StageDone(stageStart, s_awakeningStage);

        // Quest counts, log, updates, lines and markers (#433), before the inventory flush below clears the slot
        // changes the collect recount reads. Each step is contained inside the flush (a throwing recount still lets the
        // log and markers out), and the flush per connection: one character's throw costs nobody else.
        if (_quests is not null)
        {
            // The daily, weekly and monthly quest reset (#710), once per tick before any flush: one comparison against
            // the next boundary until one passes; then each flush below sees the new generation and settles its
            // character. Contained like the flushes: a throw costs this tick's reset check, not the tick.
            try
            {
                _quests.Resets.Advance(now.UtcNow);
            }
            catch (Exception e)
            {
                _questResetErrors.Failed(e);
            }

            for (int i = 0; i < conns.Length; i++)
            {
                try
                {
                    QuestFlusher.Flush(conns[i], _quests);
                }
                catch (Exception e)
                {
                    _questFlushErrors.Failed(e);
                }
            }
        }

        stageStart = StageDone(stageStart, s_questsStage);

        // Inventory and gold changed anywhere in this tick, in either pass, leave as one packet per
        // connection with each slot at its final value (spec #459 section 3). Before the ping below,
        // which has to be the last thing enqueued ahead of the flush.
        // The items generation is read once, so every connection's magic copies are named from the same one (#1179).
        ItemsPatch items = _world.Data.Items;
        for (int i = 0; i < conns.Length; i++)
            InventoryUpdateFlusher.Flush(conns[i], items, _inventoryModifierDtos);
        stageStart = StageDone(stageStart, s_inventoryStage);

        // The character sheet (#506), to its owner only, when a value it shows changed this tick: a gear
        // change, a level-up, or a combat reload applied at the top of the world update that moved a cap.
        // The combat generation is read once, so every connection is measured against the same caps.
        CombatPatch combat = _world.Data.Combat;
        for (int i = 0; i < conns.Length; i++)
            CharacterSheetFlusher.Flush(conns[i], combat);
        stageStart = StageDone(stageStart, s_sheetStage);

        // The ability list a talent change owes (#1181), once however many changes the tick made, and the talent
        // window's state, when an allocation or a paying turn-in marked it owed or the level moved; then each ability's
        // per-hit amount (#669), in the same flush as the sheet whose stats moved it. The quest
        // catalog the points are priced by is read once, so every connection is priced by the same one.
        QuestCatalog questCatalog = _world.Data.Quests;
        for (int i = 0; i < conns.Length; i++)
        {
            TalentFlusher.Flush(conns[i], questCatalog);
            AbilityAmountsFlusher.Flush(conns[i]);
        }
        stageStart = StageDone(stageStart, s_abilityAmountsStage);

        // Party members' pools (2026-09-30), to the members in their instance, at most four times a second each.
        // Contained: a throw must not cost every connection the ping, outbox and continuation flushes below.
        try
        {
            _parties.FlushMemberStatus();
        }
        catch (Exception e)
        {
            _memberStatusErrors.Failed(e);
        }

        stageStart = StageDone(stageStart, s_partyStatusStage);

        // The admin view's presence (#639), about once a second: taken here, in the serial phase after the world
        // update, so the rosters are read on the tick that changes them; the Redis write stays off the tick.
        if (_presence is not null)
        {
            try
            {
                _presence.CaptureIfDue(_world.InstanceRegistry);
            }
            catch (Exception e)
            {
                _presenceErrors.Failed(e);
            }
        }

        stageStart = StageDone(stageStart, s_presenceStage);

        // Time-sync ping (#1034): each connection keeps the tick its next turn is due on, so the sweep reads one field
        // of each and does the rest only for the connections due. A turn is the liveness rule first (an admitted
        // connection that answered no ping for a minute is closed rather than pinged again), then a ping: every 300
        // ticks on the connection's slot once the handshake has started its time sync (a turn before it is the liveness
        // rule alone), one slot after another as connections are made, so 300 connections still
        // produce about one ping a tick, whatever their place in the list, which shifts with every removal; and the
        // start burst after the handshake, on the next tick and twice more 60 ticks apart. The tick count MUST move
        // once per tick. Each ping is stamped by its send thread as it is written (#875), so where the tick queues it
        // does not move the round trip.
        long pingTick = _pingTickCounter++;
        for (int i = 0; i < typedConns.Length; i++)
        {
            WorldConnection connection = typedConns[i];
            if (pingTick >= connection.NextTimeSyncTick)
                connection.OnTimeSyncDue(pingTick, now);
        }

        stageStart = StageDone(stageStart, s_pingsStage);

        // The tick never writes (#875): each connection's packets are already queued on its sender, and one signal per
        // send thread wakes it to frame them (sealing them too while Network:PacketEncryption is on) and write them.
        _sendScheduler.SignalAll();
        stageStart = StageDone(stageStart, s_outboxStage);

        foreach (IWorldConnection worldConnection in conns)
            worldConnection.FlushContinuations();
        StageDone(stageStart, s_continuationsStage);
        long tickEnd = Stopwatch.GetTimestamp();
        double tickUs = TicksToUs(tickEnd - tickStart);
        _tickDurationHist.Record((long)tickUs);
        _tickDuration.Record(tickUs);
        // From the two timestamps the tick's duration was just measured with (#1016): no clock read of its own. A tick
        // that threw is in no window, as it is in no tick histogram.
        _tickWindow.Add(tickStart, tickEnd);

        // Last (#1066): every packet this tick built names it, and a ping stamped from here on names the next one. A tick
        // that threw is not counted.
        _ticks.Advance();
    }

    /// <summary>Records the stage that began at <paramref name="start" /> under its tag; returns now, the next stage's start.</summary>
    private long StageDone(long start, KeyValuePair<string, object?> stage)
    {
        long now = Stopwatch.GetTimestamp();
        _postUpdateDuration.Record(TicksToUs(now - start), stage);
        return now;
    }

    private static readonly double s_usPerTick = 1_000_000.0 / Stopwatch.Frequency;
    private static double TicksToUs(long t) => t * s_usPerTick;

    private bool NewConnection(IConnection connection) => true;

    internal void EnqueueDisconnect(WorldConnection connection) => _pendingDisconnects.Enqueue(connection);

    protected override object GetContextPacket(IConnection connection, object? packet, Type packetType)
    {
        Func<IConnection, Packet?, object> factory = _contextFactoryCache.GetOrAdd(packetType, static t =>
            (Func<IConnection, Packet?, object>)s_buildContextMethod.MakeGenericMethod(t).Invoke(null, null)!);
        return factory(connection, packet as Packet);
    }

    private static Func<IConnection, Packet?, object> BuildContextFactory<TPacket>() where TPacket : Packet
        => static (conn, pkt) => new WorldPacketContext<TPacket>
        { Connection = (IWorldConnection)conn!, Packet = (TPacket)pkt! };

    private void PublishCatalogOnHotReload(ScriptCatalogPublisher catalog) =>
        _scriptHotReloader.ScriptsHotReloaded += types =>
        {
            _scriptManager.RegisterHotReloaded(types);
            _ = Task.Run(catalog.PublishAsync, CancellationToken.None);
        };

    #region Cache Subscriptions

    private async Task CacheSubscribeAsync()
    {
        await _cache.SubscribeAsync(CacheKeys.WorldAccountsDisconnectChannel, DelayedDisconnect);
        await _cache.SubscribeAsync(CacheKeys.WorldAccountsStatusChannel, AccountStatusChanged);
        await _cache.SubscribeAsync(GameContextRevocations.Channel, (_, value) => NotifyGameContextRevocation(Connections, value.ToString()));
        if (_maintenanceCoordinator is not null)
            await _cache.SubscribeAsync(CacheKeys.WorldMaintenance(_world.Id.Value), MaintenanceNotice);

        // The API asks this world, and only this world, to reload after a template save. Required: a host that
        // builds a WorldServer registers the handler, so a missing one fails startup rather than going unnoticed.
        ReloadRequestHandler reloads = _services.GetRequiredService<ReloadRequestHandler>();
        await _cache.SubscribeAsync(CacheKeys.WorldReloadChannel(_world.Id.Value), reloads.OnMessage);
    }

    /// <summary>
    /// On Redis's thread. Reads the row off the tick and only offers it to the coordinator: the next tick applies
    /// it (#639), so nothing here changes world or connection state.
    /// </summary>
    private void MaintenanceNotice(RedisChannel channel, RedisValue value)
    {
        if (_maintenanceCoordinator is null || !long.TryParse(value.ToString(), NumberStyles.None,
                CultureInfo.InvariantCulture, out long revision))
        {
            return;
        }

        _ = ApplyMaintenanceNoticeAsync(revision);
    }

    private async Task ApplyMaintenanceNoticeAsync(long revision)
    {
        try
        {
            await _maintenanceCoordinator!.ApplyNotificationAsync(revision, CancellationToken.None);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "World maintenance notification could not be applied; reconciliation will retry");
        }
    }

    /// <summary>A notice requests an authoritative heartbeat. A delayed notice cannot kill a newer positive grant.</summary>
    public static int NotifyGameContextRevocation(IEnumerable<IWorldConnection> connections, string message)
    {
        if (!GameContextRevocations.TryParse(message, out AccountId? accountId, out Guid contextId)) return 0;
        int notified = 0;
        foreach (WorldConnection connection in connections.OfType<WorldConnection>())
        {
            if (connection.GameSessionLease is not { } lease || lease.Authority.AccountId != accountId ||
                (contextId != Guid.Empty && lease.GameContextId != contextId))
            {
                continue;
            }

            connection.RequestGameplayRevalidation(); ++notified;
        }
        return notified;
    }

    private void DelayedDisconnect(RedisChannel channel, RedisValue value)
    {
        CloseAccountSessions(Connections, value, _logger);
    }

    private void AccountStatusChanged(RedisChannel channel, RedisValue value)
    {
        CloseBannedOrDeactivated(Connections, value, _logger);
    }

    /// <summary>
    /// Closes every connection of the account a <see cref="CacheKeys.WorldAccountsStatusChannel"/> notice names
    /// (#882), with the reason it gives (<see cref="DisconnectReason.Banned"/> or <see cref="DisconnectReason.Deactivated"/>),
    /// and returns how many. A notice that does not parse is ignored: the ban moved the account's session epoch, so the
    /// next heartbeat of each of its sessions is refused anyway.
    /// </summary>
    public static int CloseBannedOrDeactivated(IEnumerable<IWorldConnection> connections, RedisValue message, ILogger logger)
    {
        string notice = message.ToString();
        if (!AccountStatusNotice.TryParse(notice, out AccountId? accountId, out DisconnectReason reason, out string? text))
        {
            // Anyone who can publish on the channel chooses this text: never log more than a prefix.
            logger.LogWarning("Ignored an account status notice that does not parse: {Message}",
                notice.Length > MaxLoggedMessageLength ? notice[..MaxLoggedMessageLength] : notice);
            return 0;
        }

        logger.LogInformation("Disconnecting account {AccountId}: {Reason}", accountId.Value, reason);
        return CloseAll(connections, accountId, text, reason, logger);
    }

    /// <summary>What a connection closed by an account disconnect is told (#504 review).</summary>
    public const string SessionEndedMessage = "Your session has ended. Please log in again.";

    /// <summary>The most of a rejected disconnect message that is logged.</summary>
    private const int MaxLoggedMessageLength = 64;

    /// <summary>
    /// Closes every connection in <paramref name="connections"/> held by the account
    /// <paramref name="message"/> names, and returns how many. Everything that ends an account's
    /// sessions publishes that message, but a ban or a deactivation (<see cref="CloseBannedOrDeactivated"/>, #882):
    /// a duplicate login, a password, email or role change, an MFA reset or removal, a refresh-token reuse. It is the bare account id, so it cannot say
    /// which, and every connection is told the same neutral <see cref="SessionEndedMessage"/> with
    /// <see cref="DisconnectReason.Kicked"/>, never "logged in from another location". All of them,
    /// not the first (#504 review): a second connection of the account, a duplicate session or one
    /// still closing, would otherwise keep the access the change took away. A message that names no
    /// account is ignored; one connection that throws while closing is logged and the rest are
    /// still closed.
    /// </summary>
    public static int CloseAccountSessions(IEnumerable<IWorldConnection> connections, RedisValue message, ILogger logger)
    {
        if (!long.TryParse(message.ToString(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out long id))
        {
            // Anyone who can publish on the channel chooses this text: never log more than a prefix.
            string text = message.ToString();
            logger.LogWarning("Ignored an account disconnect that names no account: {Message}",
                text.Length > MaxLoggedMessageLength ? text[..MaxLoggedMessageLength] : text);
            return 0;
        }

        logger.LogInformation("Disconnecting account {AccountId}", id);
        // Everything on this channel ends the account's sessions for its credentials or its security (a ban or a
        // deactivation has a channel of its own), not as a sanction: the place is held, as after a drop (#967, owner
        // decision). Should a ban come here (its status notice could not be published), identity removes the place
        // anyway: it reads the account's standing at the end.
        return CloseAll(connections, new AccountId(id), SessionEndedMessage, DisconnectReason.Kicked, logger,
            GameSessionEndReasons.Drop);
    }

    /// <param name="sessionEnd">
    /// What identity is told when the session ends, when it is not what <paramref name="reason"/> maps to (#967).
    /// </param>
    private static int CloseAll(IEnumerable<IWorldConnection> connections, AccountId accountId, string text,
        DisconnectReason reason, ILogger logger, string? sessionEnd = null)
    {
        int closed = 0;
        // A snapshot: closing a connection can change the collection it came from.
        foreach (IWorldConnection connection in connections.Where(c => c.AccountId == accountId).ToList())
        {
            try
            {
                if (sessionEnd is not null) SessionEndReasons.Note(connection, sessionEnd);
#pragma warning disable MA0045 // a cache subscription callback, and the process stays up to finish the close
                GracefulShutdownHelper.NotifyAndClose(connection, text, reason, logger);
#pragma warning restore MA0045
                closed++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not close a world connection of account {AccountId}", accountId.Value);
            }
        }

        return closed;
    }

    #endregion
}
