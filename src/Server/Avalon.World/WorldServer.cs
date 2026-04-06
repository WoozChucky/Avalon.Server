using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using Avalon.Common.Telemetry;
using Avalon.Common.Utils;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.World.Entities;
using Avalon.World.Filters;
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
    void Execute(WorldConnection connection, Packet packet);
}

public abstract class WorldPacketHandler<TPacket> : IWorldPacketHandler where TPacket : Packet
{
    void IWorldPacketHandler.Execute(WorldConnection connection, Packet packet) => Execute(connection, (TPacket)packet);

    public abstract void Execute(WorldConnection connection, TPacket packet);
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
    private static readonly TimeSpan MinUpdateInterval = TimeSpan.FromMilliseconds(16.6666667);
    private readonly IReplicatedCache _cache;
    private readonly ICreatureSpawner _creatureSpawner;
    private readonly Stopwatch _gameTime = new();
    private readonly ILogger<WorldServer> _logger;
    private readonly INavigationMeshBaker _navigationMeshBaker;

    private readonly ConcurrentDictionary<Type, (PropertyInfo packetProperty, PropertyInfo connectionProperty)>
        _propertyCache = new();

    private readonly IScriptHotReloader _scriptHotReloader;
    private readonly IScriptManager _scriptManager;
    private readonly Stopwatch _serverTimer = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly World _world;
    private long _lastTpsCalculationMs = TimeUtils.GetMsTime();
    private uint _realCurrentTime;
    private uint _realPreviousTime = TimeUtils.GetMsTime();
    private long _tickCount;

    private ObservableGauge<double> _tickRate;
    private double _ticksPerSecond;

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

        PacketHandlers = new Dictionary<NetworkPacketType, IWorldPacketHandler>();

        Dictionary<NetworkPacketType, Type> packetHandlers = typeof(WorldServer).Assembly.GetTypes()
            .Where(x => x.GetCustomAttribute<PacketHandlerAttribute>() != null)
            .ToDictionary(x => x.GetCustomAttribute<PacketHandlerAttribute>()!.PacketType, x => x);

        foreach ((NetworkPacketType packetType, Type handlerType) in packetHandlers)
        {
            object handler = ActivatorUtilities.CreateInstance(serviceProvider, handlerType);
            PacketHandlers.Add(packetType, (IWorldPacketHandler)handler);
            _logger.LogInformation("Registered packet handler {HandlerType} for packet type {PacketType}", handlerType,
                packetType);
        }

        _tickRate = DiagnosticsConfig.World.Meter.CreateObservableGauge(
            "world.tick.rate", () => _ticksPerSecond, "tps", "World tick rate per second");
    }

    public new ImmutableArray<IWorldConnection> Connections =>
        [.. base.Connections.Values.Cast<WorldConnection>()];

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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Tick();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Tick failed");
            }
        }

        _logger.LogInformation("World server stopped... Ran for {Minutes}mins", (int)_serverTimer.Elapsed.TotalMinutes);
    }

    protected override Task OnStoppingAsync(CancellationToken stoppingToken)
    {
        foreach (IWorldConnection connection in Connections)
            GracefulShutdownHelper.NotifyAndClose(connection, "Server is shutting down", DisconnectReason.ServerShutdown, _logger);

        return Task.CompletedTask;
    }

    private async ValueTask Tick()
    {
        _realCurrentTime = TimeUtils.GetMsTime();
        uint diff = TimeUtils.GetMsTimeDiff(_realPreviousTime, _realCurrentTime);

        const int minDeltaTime = 1; // Minimum delta time in milliseconds

        // Throttle the update loop if the diff is less than the minimum update interval
        if (diff < MinUpdateInterval.TotalMilliseconds)
        {
            double targetTime = _stopwatch.ElapsedMilliseconds + (MinUpdateInterval.TotalMilliseconds - diff);
            while (_stopwatch.ElapsedMilliseconds < targetTime)
            {
                if (_stopwatch.ElapsedMilliseconds + 1 < targetTime)
                {
                    await Task.Yield(); // Yield control briefly for longer waits
                }
                else
                {
                    Thread.SpinWait(1); // Fine-grained spinning for short waits
                }
            }

            _realCurrentTime = TimeUtils.GetMsTime();
            diff = TimeUtils.GetMsTimeDiff(_realPreviousTime, _realCurrentTime);
        }

        // Ensure delta time is at least the minimum threshold
        if (diff < minDeltaTime)
        {
            diff = minDeltaTime;
        }

        Update(TimeSpan.FromMilliseconds(diff));

        _realPreviousTime = _realCurrentTime;

        // Tick Rate Calculations
        _tickCount++;
        double elapsedSeconds = (_stopwatch.ElapsedMilliseconds - _lastTpsCalculationMs) / 1000.0;

        if (elapsedSeconds >= 1.0)
        {
            _ticksPerSecond = _tickCount / elapsedSeconds;
            _tickCount = 0;
            _lastTpsCalculationMs = _stopwatch.ElapsedMilliseconds;
        }
    }

    private void Update(TimeSpan elapsedTime)
    {
        foreach (IWorldConnection worldConnection in Connections)
        {
            WorldSessionFilter filter = new(worldConnection);

            worldConnection.Update(elapsedTime, filter);
        }

        _world.Update(elapsedTime);
    }

    private bool NewConnection(IConnection connection) => true;

    public async Task ClearInWorldFlagAsync(AccountId? accountId)
    {
        if (accountId is null)
            return;
        await _cache.RemoveAsync($"account:{accountId}:inWorld");
    }

    protected override object GetContextPacket(IConnection connection, object? packet, Type packetType)
    {
        // Check if the cache contains the property accessors for the given packet type
        if (!_propertyCache.TryGetValue(packetType,
                out (PropertyInfo packetProperty, PropertyInfo connectionProperty) cachedProperties))
        {
            // Cache miss: Reflect the properties
            PropertyInfo contextPacketProperty = typeof(WorldPacketContext<>).MakeGenericType(packetType)
                .GetProperty(nameof(WorldPacketContext<object>.Packet))!;
            PropertyInfo contextConnectionProperty = typeof(WorldPacketContext<>).MakeGenericType(packetType)
                .GetProperty(nameof(WorldPacketContext<object>.Connection))!;

            // Cache the reflected properties
            cachedProperties = (contextPacketProperty, contextConnectionProperty);
            _propertyCache[packetType] = cachedProperties;
        }

        // Create a new context instance
        object context = Activator.CreateInstance(typeof(WorldPacketContext<>).MakeGenericType(packetType))!;

        // Set the packet and connection properties
        cachedProperties.packetProperty.SetValue(context, packet);
        cachedProperties.connectionProperty.SetValue(context, connection);

        return context;
    }

    #region Cache Subscriptions

    private async Task CacheSubscribeAsync() =>
        await _cache.SubscribeAsync("world:accounts:disconnect", DelayedDisconnect);

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
