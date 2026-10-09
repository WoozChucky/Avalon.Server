using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalon.Common.Telemetry;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Domain.World;
using Avalon.Hosting.Networking;
using Avalon.Hosting.Telemetry;
using Avalon.Infrastructure;
using Avalon.Infrastructure.GameAuth;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.World.Characters;
using Avalon.World.GameAuth;
using Avalon.World.Inventory;
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
}

public class WorldServer : ServerBase<WorldConnection>, IWorldServer
{
    public IGameAdmissionClient AdmissionClient => _services.GetRequiredService<IGameAdmissionClient>();
    public WorldTlsTransport TlsTransport => _services.GetRequiredService<WorldTlsTransport>();

    // Invoked on the tick so despawn removes the entity before any further world simulation.
    internal async Task DrainGameplayAsync(WorldConnection connection)
    {
        await _world.DeSpawnPlayerAsync(connection).ConfigureAwait(false);
        try { await connection.WhenPendingOperationsIdle().ConfigureAwait(false); } catch (Exception) { }
        Task<WorldAdmissionResult>? pendingAdmission = connection.AdmissionWork;
        if (pendingAdmission is not null)
            try { await pendingAdmission.ConfigureAwait(false); } catch (Exception) { }
        GameSessionLease? lease = connection.GameSessionLease ?? (pendingAdmission is { IsCompletedSuccessfully: true } ? pendingAdmission.Result.Lease : null);
        if (lease is not null)
            await Task.Run(() => AdmissionClient.EndAsync(lease, CancellationToken.None), CancellationToken.None).ConfigureAwait(false);
    }
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
    private static readonly long s_ticksPerFrame = Stopwatch.Frequency / 60; // 60Hz
    private static readonly long s_spinThresholdTicks = Stopwatch.Frequency / 1000; // 1ms

    private Thread? _tickThread;
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
    private readonly ThrottledErrorLog _memberStatusErrors;
    private readonly QuestService? _quests;
    private readonly ThrottledErrorLog _questFlushErrors;
    private readonly IScriptHotReloader _scriptHotReloader;
    private readonly IScriptManager _scriptManager;
    private readonly Stopwatch _serverTimer = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
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
    private readonly ConcurrentQueue<WorldConnection> _pendingDisconnects = new();
    private long _lastTpsCalculationMs;
    private long _tickCount;

    // 60Hz × 10s = 600 ticks. Spread pings across ticks so all connections don't fire
    // in the same tick — each connection gets its own offset based on its index.
    private const int TimeSyncTicksPeriod = 600;

    // Monotonic tick counter used solely to derive the time-sync ping phase.
    // We can't reuse _tickCount because that one resets every ~1s for the TPS calculation.
    private long _pingTickCounter;
    private long _completedTicks;
    private volatile bool _isListening;

    public bool IsListening => _isListening;
    public long CompletedTicks => Interlocked.Read(ref _completedTicks);

    private Histogram<double> _tickDuration;
    private Histogram<double> _deadlineOvershoot;
    private Histogram<double> _worldUpdateDuration;
    private Histogram<double> _sessionUpdateDuration;
    private Histogram<double> _postUpdateDuration;
    private double _ticksPerSecond;

    // The stages after the world update (#875), one tag each, cached so timing a stage allocates nothing.
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
        WorldMaintenanceCoordinator? maintenanceCoordinator = null) : base(packetManager, loggerFactory.CreateLogger<WorldServer>(),
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
        _logger = loggerFactory.CreateLogger<WorldServer>();
        _memberStatusErrors = new ThrottledErrorLog(_logger,
            serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System, "The party member status flush");
        // #433. Optional here, like the clock above, so the tests that build a WorldServer from a bare provider still do.
        _quests = serviceProvider.GetService<QuestService>();
        _questFlushErrors = new ThrottledErrorLog(_logger,
            serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System, "The quest flush");
        _world = world;
        // Optional, like the clock: the tests that build a WorldServer from a bare provider have none.
        _tickThreadGuard = serviceProvider.GetService<TickThreadGuard>();
        _presence = serviceProvider.GetService<PresenceCapture>();
        _presenceErrors = new ThrottledErrorLog(_logger,
            serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System, "The presence capture");
        _time = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
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
            "world.tick.rate", () => _ticksPerSecond, "tps", "World tick rate per second");

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
            "Duration of one stage after the world update (flushers, pings, outbox flush, continuations) in microseconds, by stage",
            WorldHistograms.StageMicroseconds);

        WorldGauges.Register(DiagnosticsConfig.World.Meter, () => Connections, () => _world.InstanceRegistry);
    }

    public new ImmutableArray<IWorldConnection> Connections =>
        TypedConnections.CastArray<IWorldConnection>();

    protected override PacketTags DescribeConnection(IConnection connection) =>
        connection is WorldConnection world ? world.TelemetryTags() : base.DescribeConnection(connection);

    public IWorld World => _world;

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

        // The restart drain (#768) first, with the listener already stopped: warn the players, and wait until they
        // have left or the drain's deadline has passed. Its countdown and cutoff run on the tick (#639), so the tick
        // keeps running until it ends. Only a running tick can end it; a stop during the load has none, and skips it.
        if (_maintenanceCoordinator is not null && _tickRunning && _tickThread is { IsAlive: true })
            await _maintenanceCoordinator.DrainForRestartAsync(stoppingToken).ConfigureAwait(false);

        // The tick goes first. Closing an outbox does its own final flush, so the tick has nothing
        // left to contribute, and letting it keep flushing outboxes that are mid-teardown would
        // put a second writer on buffers the close is about to hand back to the pool.
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
        // and cutting it short would discard the save this exists to make.
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

        try
        {
            await Task.WhenAll(despawning).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // A despawn's task does not fault today; if one ever does, the wait for saves below still runs.
            _logger.LogError(e, "A despawn at shutdown failed");
        }

        // The pass above only covers despawns it started. A tick starts each despawn without
        // waiting for it, so one begun on an earlier tick can still be queued behind another save,
        // or be mid-transaction, and the host disposes the database as soon as this returns. The
        // periodic saves in flight are the same. Bounded by the host's own stop timeout and by
        // SaveDrainLimit, because a write that never returns must not hold the process up forever.
        await WaitForSavesAsync(stoppingToken).ConfigureAwait(false);

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

    private async Task WaitForSavesAsync(CancellationToken stoppingToken)
    {
        Task saves = _characterSaver.WhenAllIdle();
        if (saves.IsCompleted)
            return;

        try
        {
            await saves.WaitAsync(SaveDrainLimit, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException)
        {
            _logger.LogError(
                "Shutdown stopped waiting for character saves still in flight after {Limit} or at the host's stop timeout; " +
                "changes since those characters' last committed save may be lost",
                SaveDrainLimit);
        }
    }

    private void TickLoop()
    {
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

                    Update(deltaTime, tickStart);

                    Interlocked.Increment(ref _completedTicks);

                    _tickCount++;
                    double elapsedSeconds =
                        (_stopwatch.ElapsedMilliseconds - _lastTpsCalculationMs) / 1000.0;
                    if (elapsedSeconds >= 1.0)
                    {
                        _ticksPerSecond = _tickCount / elapsedSeconds;
                        _tickCount = 0;
                        _lastTpsCalculationMs = _stopwatch.ElapsedMilliseconds;
                    }
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
        long t0 = Stopwatch.GetTimestamp();
        // Process disconnects on the tick thread to avoid racing with MapInstance.Update
        while (_pendingDisconnects.TryDequeue(out WorldConnection? disconnected))
        {
            _ = disconnected.DrainGameplayAsync();
        }

        // Cache once — both phases iterate the same set.
        ImmutableArray<IWorldConnection> conns = Connections;

        // Revoked or expiring sessions leave their instance before packets and simulation run.
        foreach (IWorldConnection connection in conns)
            if (connection is WorldConnection admitted) admitted.AdvanceGameplayLease();

        // Before the session pass, so a cutoff blocks a non-Admin's queued packets on the tick it arrives. Applies a
        // state the notification or the reconciliation offered from off the tick. Contained: the tick goes on.
        try
        {
            _maintenanceCoordinator?.Advance(_time.GetUtcNow().UtcDateTime, conns);
        }
        catch (Exception e)
        {
            _maintenanceErrors.Failed(e);
        }

        foreach (IWorldConnection worldConnection in conns)
            worldConnection.UpdateSession();
        long t1 = Stopwatch.GetTimestamp();
        double sessionUs = TicksToUs(t1 - t0);
        _sessionUpdateHist.Record((long)sessionUs);
        _sessionUpdateDuration.Record(sessionUs);

        // After the session pass, so a load report that arrived this tick releases its own barrier
        // rather than being beaten to it, and before the world update, so a character released here
        // is simulated on the tick that released it.
        long barrierNowTicks = _time.GetUtcNow().UtcTicks;
        var barrierTimeout = TimeSpan.FromSeconds(_world.Configuration.CharacterLoadTimeoutSeconds);

        CharacterReadinessBarrier.ReleaseExpired(conns, _world, barrierNowTicks, barrierTimeout, _logger,
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

        // Quest counts, log, updates, lines and markers (#433), before the inventory flush below clears the slot
        // changes the collect recount reads. Each step is contained inside the flush (a throwing recount still lets the
        // log and markers out), and the flush per connection: one character's throw costs nobody else.
        if (_quests is not null)
        {
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

        // Inventory and money changed anywhere in this tick, in either pass, leave as one packet per
        // connection with each slot at its final value (spec #459 section 3). Before the ping below,
        // which has to be the last thing enqueued ahead of the flush.
        for (int i = 0; i < conns.Length; i++)
            InventoryUpdateFlusher.Flush(conns[i]);
        stageStart = StageDone(stageStart, s_inventoryStage);

        // The character sheet (#506), to its owner only, when a value it shows changed this tick: a gear
        // change, a level-up, or a combat reload applied at the top of the world update that moved a cap.
        // The formula is read once, so every connection is measured against the same caps.
        CombatFormula formula = _world.Data.Combat.Formula;
        for (int i = 0; i < conns.Length; i++)
            CharacterSheetFlusher.Flush(conns[i], formula);
        stageStart = StageDone(stageStart, s_sheetStage);

        // Each ability's per-hit amount (#669), in the same flush as the sheet whose stats moved it.
        for (int i = 0; i < conns.Length; i++)
            AbilityAmountsFlusher.Flush(conns[i]);
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

        // Time-sync ping: stagger across the 600-tick window using each connection's
        // list index, so 600 connections still produce only ~1 ping/tick worst case.
        // Phase MUST come from a monotonic counter — _tickCount above resets every ~1s.
        // ENQUEUED BEFORE THE FLUSH BELOW, because SendTimeSyncPing stamps the send time it will
        // later measure the round trip against. Flushed a tick later, that stamp is ~16 ms old
        // before the packet leaves, and every reported round trip carries the difference.
        long phase = _pingTickCounter++ % TimeSyncTicksPeriod;
        for (int i = 0; i < conns.Length; i++)
        {
            // A connection that has just handshaken pings on this tick whatever its phase, so the
            // FIRST round trip a client is told is stamped beside the flush like every other one.
            if (conns[i].TakeInitialTimeSyncPingRequest() || i % TimeSyncTicksPeriod == phase)
                conns[i].SendTimeSyncPing();
        }

        stageStart = StageDone(stageStart, s_pingsStage);

        for (int i = 0; i < conns.Length; i++)
            conns[i].FlushOutbox();
        stageStart = StageDone(stageStart, s_outboxStage);

        foreach (IWorldConnection worldConnection in conns)
            worldConnection.FlushContinuations();
        StageDone(stageStart, s_continuationsStage);
        long tickEnd = Stopwatch.GetTimestamp();
        double tickUs = TicksToUs(tickEnd - tickStart);
        _tickDurationHist.Record((long)tickUs);
        _tickDuration.Record(tickUs);
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

    /// <summary>What a connection closed by an account disconnect is told (#504 review).</summary>
    public const string SessionEndedMessage = "Your session has ended. Please log in again.";

    /// <summary>The most of a rejected disconnect message that is logged.</summary>
    private const int MaxLoggedMessageLength = 64;

    /// <summary>
    /// Closes every connection in <paramref name="connections"/> held by the account
    /// <paramref name="message"/> names, and returns how many. Everything that ends an account's
    /// sessions publishes that message: a duplicate login, a password, email or role change, an MFA
    /// reset or removal, a ban, a refresh-token reuse. It is the bare account id, so it cannot say
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
        var accountId = new AccountId(id);
        int closed = 0;
        // A snapshot: closing a connection can change the collection it came from.
        foreach (IWorldConnection connection in connections.Where(c => c.AccountId == accountId).ToList())
        {
            try
            {
#pragma warning disable MA0045 // a cache subscription callback, and the process stays up to finish the close
                GracefulShutdownHelper.NotifyAndClose(connection, SessionEndedMessage, DisconnectReason.Kicked, logger);
#pragma warning restore MA0045
                closed++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not close a world connection of account {AccountId}", id);
            }
        }

        return closed;
    }

    #endregion
}
