using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalon.Common.Telemetry;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.World.Entities;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abstractions;
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
    IWorld World { get; }
    Dictionary<NetworkPacketType, IWorldPacketHandler> PacketHandlers { get; }
}

public class WorldServer : ServerBase<WorldConnection>, IWorldServer
{
    #region Scheduling (Move to HighRes Timer class)

    [SupportedOSPlatform("windows")]
    private static IntPtr CreateHighResTimer()
    {
        const uint TIMER_ALL_ACCESS = 0x1F0003;
        const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
        IntPtr h = CreateWaitableTimerExW(IntPtr.Zero, null,
            CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
        if (h == IntPtr.Zero)
            throw new InvalidOperationException(
                "High-resolution waitable timer unavailable (requires Windows 10 1803+).");
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
    private static readonly long TicksPerFrame = Stopwatch.Frequency / 60; // 60Hz
    private static readonly long SpinThresholdTicks = Stopwatch.Frequency / 1000; // 1ms

    private Thread? _tickThread;
    private volatile bool _tickRunning;
    private IntPtr _waitableTimer; // Windows high-res timer handle
    private readonly TaskCompletionSource _tickExited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly IReplicatedCache _cache;
    private readonly ConcurrentDictionary<Type, Func<IConnection, Packet?, object>>
        _contextFactoryCache = new();
    private readonly ICreatureSpawner _creatureSpawner;
    private readonly Stopwatch _gameTime = new();
    private readonly ILogger<WorldServer> _logger;
    private readonly INavigationMeshBaker _navigationMeshBaker;
    private readonly IScriptHotReloader _scriptHotReloader;
    private readonly IScriptManager _scriptManager;
    private readonly Stopwatch _serverTimer = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly World _world;
    private readonly ConcurrentQueue<WorldConnection> _pendingDisconnects = new();
    private long _lastTpsCalculationMs;
    private long _tickCount;

    private ObservableGauge<double> _tickRate;
    private Histogram<double> _tickDuration;
    private Histogram<double> _deadlineOvershoot;
    private Histogram<double> _worldUpdateDuration;
    private Histogram<double> _sessionUpdateDuration;
    private double _ticksPerSecond;
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
        ICreatureSpawner creatureSpawner,
        IReplicatedCache cache,
        IScriptHotReloader scriptHotReloader,
        INavigationMeshBaker navigationMeshBaker) : base(packetManager, loggerFactory.CreateLogger<WorldServer>(),
        serviceProvider,
        hostingOptions)
    {
        _scriptManager = scriptManager;
        _creatureSpawner = creatureSpawner;
        _cache = cache;
        _scriptHotReloader = scriptHotReloader;
        _navigationMeshBaker = navigationMeshBaker;
        _logger = loggerFactory.CreateLogger<WorldServer>();
        _world = world as World ?? throw new InvalidOperationException("Invalid world instance");
        
        _logger.LogInformation("R2R enabled: {R2R}",
            System.Runtime.CompilerServices.RuntimeFeature.IsSupported("IsDynamicCodeCompiled"));

        PacketHandlers = new Dictionary<NetworkPacketType, IWorldPacketHandler>();

        Dictionary<NetworkPacketType, Type> packetHandlers = typeof(WorldServer).Assembly.GetTypes()
            .Where(x => x.GetCustomAttribute<PacketHandlerAttribute>() != null)
            .ToDictionary(x => x.GetCustomAttribute<PacketHandlerAttribute>()!.PacketType, x => x);

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

        _tickRate = DiagnosticsConfig.World.Meter.CreateObservableGauge(
            "world.tick.rate", () => _ticksPerSecond, "tps", "World tick rate per second");

        _tickDuration = DiagnosticsConfig.World.Meter.CreateHistogram<double>("world.tick.duration", "us",
                "Duration of a world tick in microseconds");
        _deadlineOvershoot = DiagnosticsConfig.World.Meter.CreateHistogram<double>("world.tick.deadline_overshoot", "us",
            "How much the tick loop overshot its deadline (positive) or woke early (negative), in microseconds");
        _worldUpdateDuration = DiagnosticsConfig.World.Meter.CreateHistogram<double>("world.update.duration", "us",
            "Duration of the world update phase of the tick loop in microseconds");
        _sessionUpdateDuration = DiagnosticsConfig.World.Meter.CreateHistogram<double>("world.session_update.duration", "us",
            "Duration of the session update phase of the tick loop in microseconds");
    }

    public new ImmutableArray<IWorldConnection> Connections =>
        TypedConnections.CastArray<IWorldConnection>();

    public IWorld World => _world;
    public Dictionary<NetworkPacketType, IWorldPacketHandler> PacketHandlers { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(
            _navigationMeshBaker.ExecuteAsync(),
            Task.Run(() => _scriptManager.Load(), stoppingToken),
            _creatureSpawner.LoadAsync()
        );

        await _world.LoadAsync(stoppingToken);

        _scriptHotReloader.Start();

        await CacheSubscribeAsync();

        RegisterNewConnectionListener(NewConnection);

        _serverTimer.Start();

        StartListening();

        _gameTime.Start();

        if (OperatingSystem.IsWindows())
            _waitableTimer = CreateHighResTimer();

        _tickRunning = true;
        _tickThread = new Thread(TickLoop)
        {
            Name = "WorldServer.Tick",
            IsBackground = false,
            Priority = ThreadPriority.Highest,
        };
        _tickThread.Start();

        // When the host signals shutdown, stop the tick loop.
        // 1 frame (~16ms) of shutdown latency is acceptable and avoids
        // a second sync primitive to wake the timer early.
        await using var _ = stoppingToken.Register(static state =>
            ((WorldServer)state!)._tickRunning = false, this);

        await _tickExited.Task;

        _logger.LogInformation("World server stopped... Ran for {Minutes}mins",
            (int)_serverTimer.Elapsed.TotalMinutes);
    }

    protected override Task OnStoppingAsync(CancellationToken stoppingToken)
    {
        foreach (IWorldConnection connection in Connections)
            GracefulShutdownHelper.NotifyAndClose(connection, "Server is shutting down", DisconnectReason.ServerShutdown, _logger);

        // Wait for tick loop to drain (should already be exiting via token registration)
        _tickRunning = false;
        if (_tickThread is not null && _tickThread.IsAlive)
            _tickThread.Join(TimeSpan.FromSeconds(5));

        if (_waitableTimer != IntPtr.Zero)
        {
            CloseHandle(_waitableTimer);
            _waitableTimer = IntPtr.Zero;
        }

        return Task.CompletedTask;
    }

    private void TickLoop()
    {
        long next = Stopwatch.GetTimestamp() + TicksPerFrame;
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
                    long overshootTicks = tickStart - (next - TicksPerFrame);
                    double overshootUs = TicksToUs(overshootTicks);
                    _deadlineOvershootHist.Record((long)overshootUs);
                    _deadlineOvershoot.Record(overshootUs);

                    Update(deltaTime, tickStart);

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
                if (remaining > SpinThresholdTicks)
                {
                    long sleepTicks = remaining - SpinThresholdTicks;
                    if (OperatingSystem.IsWindows() && _waitableTimer != IntPtr.Zero)
                        WaitHighRes(_waitableTimer, sleepTicks);
                    else
                        Thread.Sleep(TimeSpan.FromMilliseconds(
                            sleepTicks * 1000.0 / Stopwatch.Frequency));
                }

                // ...then spin the final ~1ms for precision.
                while (Stopwatch.GetTimestamp() < next)
                    Thread.SpinWait(64);

                // Deadline-based, not delta-based: prevents drift.
                next += TicksPerFrame;

                // Spiral-of-death guard: if we fell >4 frames behind
                // (GC pause, huge packet burst), resync instead of catching up.
                long lag = Stopwatch.GetTimestamp() - next;
                if (lag > TicksPerFrame * 4)
                {
                    _logger.LogWarning("Tick loop fell {LagMs}ms behind; resyncing",
                        lag * 1000 / Stopwatch.Frequency);
                    next = Stopwatch.GetTimestamp() + TicksPerFrame;
                }
            }
        }
        finally
        {
            _tickExited.TrySetResult();
        }
    }

    private void Update(TimeSpan elapsedTime, long tickStart)
    {
        long t0 = Stopwatch.GetTimestamp();
        // Process disconnects on the tick thread to avoid racing with MapInstance.Update
        while (_pendingDisconnects.TryDequeue(out WorldConnection? disconnected))
        {
            _ = _world.DeSpawnPlayerAsync(disconnected);
        }

        // Cache once — both phases iterate the same set.
        ImmutableArray<IWorldConnection> conns = Connections;

        foreach (IWorldConnection worldConnection in conns)
            worldConnection.UpdateSession();
        long t1 = Stopwatch.GetTimestamp();
        double sessionUs = TicksToUs(t1 - t0);
        _sessionUpdateHist.Record((long)sessionUs);
        _sessionUpdateDuration.Record(sessionUs);

        _world.Update(elapsedTime);
        long t2 = Stopwatch.GetTimestamp();
        double worldUs = TicksToUs(t2 - t1);
        _worldUpdateHist.Record((long)worldUs);
        _worldUpdateDuration.Record(worldUs);

        foreach (IWorldConnection worldConnection in conns)
            worldConnection.FlushContinuations();
        long tickEnd = Stopwatch.GetTimestamp();
        double tickUs = TicksToUs(tickEnd - tickStart);
        _tickDurationHist.Record((long)tickUs);
        _tickDuration.Record(tickUs);
    }

    private static readonly double s_usPerTick = 1_000_000.0 / Stopwatch.Frequency;
    static double TicksToUs(long t) => t * s_usPerTick;

    private bool NewConnection(IConnection connection) => true;

    internal void EnqueueDisconnect(WorldConnection connection) => _pendingDisconnects.Enqueue(connection);

    public async Task ClearInWorldFlagAsync(AccountId? accountId)
    {
        if (accountId is null)
            return;
        await _cache.RemoveAsync($"account:{accountId}:inWorld");
    }

    protected override object GetContextPacket(IConnection connection, object? packet, Type packetType)
    {
        var factory = _contextFactoryCache.GetOrAdd(packetType, static t =>
            (Func<IConnection, Packet?, object>)s_buildContextMethod.MakeGenericMethod(t).Invoke(null, null)!);
        return factory(connection, packet as Packet);
    }

    private static Func<IConnection, Packet?, object> BuildContextFactory<TPacket>() where TPacket : Packet
        => static (conn, pkt) => new WorldPacketContext<TPacket>
            { Connection = (IWorldConnection)conn!, Packet = (TPacket)pkt! };

    #region Cache Subscriptions

    private async Task CacheSubscribeAsync() =>
        await _cache.SubscribeAsync(CacheKeys.WorldAccountsDisconnectChannel, DelayedDisconnect);

    private void DelayedDisconnect(RedisChannel channel, RedisValue value)
    {
        _logger.LogInformation("Disconnecting account {AccountId}", value);
        AccountId accountId = value.ToString();

        IWorldConnection? connection = Connections.FirstOrDefault(c => c.AccountId == accountId);
        if (connection is null) return;

        GracefulShutdownHelper.NotifyAndClose(connection, "Your account has been logged in from another location.", DisconnectReason.DuplicateLogin, _logger);
    }

    #endregion
}
