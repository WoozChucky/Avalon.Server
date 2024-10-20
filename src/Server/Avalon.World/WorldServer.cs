using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using Avalon.Common.Utils;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
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
    public abstract void Execute(WorldConnection connection, TPacket packet);

    void IWorldPacketHandler.Execute(WorldConnection connection, Packet packet)
    {
        Execute(connection, (TPacket)packet);
    }
}

public class PacketHandlerAttribute : Attribute
{
    public NetworkPacketType PacketType { get; }

    public PacketHandlerAttribute(NetworkPacketType packetType)
    {
        PacketType = packetType;
    }
}


public interface IWorldServer
{
    ImmutableArray<IWorldConnection> Connections { get; }
    IWorld World { get; }
    Dictionary<NetworkPacketType, IWorldPacketHandler> PacketHandlers { get; }
}

public class WorldServer : ServerBase<WorldConnection>, IWorldServer
{
    public new ImmutableArray<IWorldConnection> Connections =>
        [.. base.Connections.Values.Cast<WorldConnection>()];

    public IWorld World => _world;
    public Dictionary<NetworkPacketType, IWorldPacketHandler> PacketHandlers { get; }

    private readonly ConcurrentDictionary<Type, (PropertyInfo packetProperty, PropertyInfo connectionProperty)> _propertyCache = new();
    private readonly ILogger<WorldServer> _logger;
    private readonly World _world;
    private readonly Stopwatch _gameTime = new();
    private readonly Stopwatch _serverTimer = new();
    private readonly IScriptManager _scriptManager;
    private readonly ICreatureSpawner _creatureSpawner;
    private readonly IReplicatedCache _cache;
    private readonly IScriptHotReloader _scriptHotReloader;
    private readonly INavigationMeshBaker _navigationMeshBaker;

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

        var packetHandlers = typeof(WorldServer).Assembly.GetTypes()
            .Where(x => x.GetCustomAttribute<PacketHandlerAttribute>() != null)
            .ToDictionary(x => x.GetCustomAttribute<PacketHandlerAttribute>()!.PacketType, x => x);

        foreach (var (packetType, handlerType) in packetHandlers)
        {
            var handler = ActivatorUtilities.CreateInstance(serviceProvider, handlerType);
            PacketHandlers.Add(packetType, (IWorldPacketHandler)handler);
            _logger.LogInformation("Registered packet handler {HandlerType} for packet type {PacketType}", handlerType, packetType);
        }
    }

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
        foreach (var connection in Connections)
        {
            // TODO: Send a disconnect packet
            connection.Close();
        }
        return Task.CompletedTask;
    }

    private uint _realCurrentTime = 0;
    private uint _realPreviousTime = TimeUtils.GetMSTime();

    private int _tickCount = 0;
    private DateTime _lastTpsCalculationTime = DateTime.Now;
    private double _ticksPerSecond = 0;

    private static readonly TimeSpan MinUpdateInterval = TimeSpan.FromMilliseconds(10); // Example minimum interval
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    private async ValueTask Tick()
    {
        _realCurrentTime = TimeUtils.GetMSTime();
        var diff = TimeUtils.GetMSTimeDiff(_realPreviousTime, _realCurrentTime);

        const int minDeltaTime = 1; // Minimum delta time in milliseconds

        // Throttle the update loop if the diff is less than the minimum update interval
        if (diff < MinUpdateInterval.TotalMilliseconds)
        {
            var targetTime = _stopwatch.ElapsedMilliseconds + (MinUpdateInterval.TotalMilliseconds - diff);

            while (_stopwatch.ElapsedMilliseconds < targetTime)
            {
                await Task.Delay(1); // Using 1ms to yield control and prevent tight loop, actual wait time is determined by _stopwatch
            }

            _realCurrentTime = TimeUtils.GetMSTime();
            diff = TimeUtils.GetMSTimeDiff(_realPreviousTime, _realCurrentTime);
        }

        // Ensure delta time is at least the minimum threshold
        if (diff < minDeltaTime)
        {
            diff = minDeltaTime;
        }

        Update(TimeSpan.FromMilliseconds(diff));

        _realPreviousTime = _realCurrentTime;

        //TODO(Nuno): Make this configurable
        if (false /* Throttle TPS calculation */)
#pragma warning disable CS0162 // Unreachable code detected
        {
            // Increment the tick counter
            _tickCount++;

            // Calculate TPS every second
            if ((DateTime.Now - _lastTpsCalculationTime).TotalSeconds >= 1)
            {
                _ticksPerSecond = _tickCount / (DateTime.Now - _lastTpsCalculationTime).TotalSeconds;
                // _logger.LogInformation("Ticks Per Second (TPS): {TPS}", ticksPerSecond);
                _tickCount = 0;
                _lastTpsCalculationTime = DateTime.Now;
            }
        }
#pragma warning restore CS0162 // Unreachable code detected
    }

    private void Update(TimeSpan elapsedTime)
    {
        foreach (var worldConnection in Connections)
        {
            var filter = new WorldSessionFilter(worldConnection);

            worldConnection.Update(elapsedTime, filter);
        }

        _world.Update(elapsedTime);
    }

    private bool NewConnection(IConnection connection)
    {
        return true;
    }

    protected override object GetContextPacket(IConnection connection, object? packet, Type packetType)
    {
        // Check if the cache contains the property accessors for the given packet type
        if (!_propertyCache.TryGetValue(packetType, out var cachedProperties))
        {
            // Cache miss: Reflect the properties
            var contextPacketProperty = typeof(WorldPacketContext<>).MakeGenericType(packetType)
                .GetProperty(nameof(WorldPacketContext<object>.Packet))!;
            var contextConnectionProperty = typeof(WorldPacketContext<>).MakeGenericType(packetType)
                .GetProperty(nameof(WorldPacketContext<object>.Connection))!;

            // Cache the reflected properties
            cachedProperties = (contextPacketProperty, contextConnectionProperty);
            _propertyCache[packetType] = cachedProperties;
        }

        // Create a new context instance
        var context = Activator.CreateInstance(typeof(WorldPacketContext<>).MakeGenericType(packetType))!;

        // Set the packet and connection properties
        cachedProperties.packetProperty.SetValue(context, packet);
        cachedProperties.connectionProperty.SetValue(context, connection);

        return context;
    }

    #region Cache Subscriptions
    private async Task CacheSubscribeAsync()
    {
        await _cache.SubscribeAsync("world:accounts:disconnect", DelayedDisconnect);
    }

    private void DelayedDisconnect(RedisChannel channel, RedisValue value)
    {
        _logger.LogInformation("Disconnecting account {AccountId}", value);
        AccountId accountId = value.ToString();

        // TODO: Send in game packet telling the player they are being disconnected
        Connections.FirstOrDefault(c => c.AccountId == accountId)?.Close();
    }
    #endregion
}
