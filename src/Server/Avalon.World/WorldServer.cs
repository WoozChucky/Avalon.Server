using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using Avalon.Common.Utils;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Hosting;
using Avalon.Hosting.Networking;
using Avalon.Hosting.PluginTypes;
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
        [..base.Connections.Values.Cast<WorldConnection>()];

    public IWorld World => _world;
    public Dictionary<NetworkPacketType, IWorldPacketHandler> PacketHandlers { get; }
    public new Dictionary<Type, PacketHandlerCache> HandlerCache => base.HandlerCache;

    private readonly ConcurrentDictionary<Type, (PropertyInfo packetProperty, PropertyInfo connectionProperty)> _propertyCache = new();
    private readonly ILogger<WorldServer> _logger;
    private readonly PluginExecutor _pluginExecutor;
    private readonly World _world;

    #region Server Timers
    private readonly Stopwatch _gameTime = new Stopwatch();
    private long _previousTicks = 0;
    private TimeSpan _accumulatedElapsedTime;
    private readonly TimeSpan _targetElapsedTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60); // 60hz
    private readonly TimeSpan _maxElapsedTime = TimeSpan.FromMilliseconds(500);
    private readonly Stopwatch _serverTimer = new();
    private readonly IAiController _aiController;
    private readonly ICreatureSpawner _creatureSpawner;
    private readonly IReplicatedCache _cache;
    private readonly IScriptHotReloader _scriptHotReloader;
    private readonly INavigationMeshBaker _navigationMeshBaker;

    public WorldServer(IPacketManager packetManager,
        ILoggerFactory loggerFactory,
        PluginExecutor pluginExecutor,
        IServiceProvider serviceProvider,
        IOptions<HostingConfiguration> hostingOptions,
        IWorld world,
        IAiController aiController,
        ICreatureSpawner creatureSpawner,
        IReplicatedCache cache,
        IScriptHotReloader scriptHotReloader,
        INavigationMeshBaker navigationMeshBaker) : base(packetManager, loggerFactory.CreateLogger<WorldServer>(), pluginExecutor,
        serviceProvider,
        hostingOptions)
    {
        _aiController = aiController;
        _creatureSpawner = creatureSpawner;
        _cache = cache;
        _scriptHotReloader = scriptHotReloader;
        _navigationMeshBaker = navigationMeshBaker;
        _logger = loggerFactory.CreateLogger<WorldServer>();
        _pluginExecutor = pluginExecutor;
        _world = world as World ?? throw new InvalidOperationException("Invalid world instance");

        PacketHandlers = new Dictionary<NetworkPacketType, IWorldPacketHandler>()
        {
            //{NetworkPacketType.CMSG_CHARACTER_LIST, a},
        };

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

    #endregion
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(
            _navigationMeshBaker.ExecuteAsync(),
            _aiController.LoadAsync(),
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
                await _pluginExecutor.ExecutePlugins<IGameTickListener>(x => x.PreUpdateAsync(stoppingToken));
                await Tick();
                await _pluginExecutor.ExecutePlugins<IGameTickListener>(x => x.PostUpdateAsync(stoppingToken));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Tick failed");
            }
        }
        
        _logger.LogInformation("World server stopped... Ran for {Minutes}mins", (int) _serverTimer.Elapsed.TotalMinutes);
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

    private uint minUpdateDiff = 1;
    private uint realCurrentTime = 0;
    private uint realPreviousTime = TimeUtils.GetMSTime();

    private uint maxCoreStuckTime = 60000;
    private uint halfMaxCoreStuckTime = 30000;
    
    private int tickCount = 0;
    private DateTime lastTpsCalculationTime = DateTime.Now;
    private double ticksPerSecond = 0;
    
    private async ValueTask Tick()
    {
        realCurrentTime = TimeUtils.GetMSTime();

        var diff = TimeUtils.GetMSTimeDiff(realPreviousTime, realCurrentTime);
        if (diff < minUpdateDiff)
        {
            var sleepTime = minUpdateDiff - diff;
            if (sleepTime >= halfMaxCoreStuckTime)
                _logger.LogWarning("WorldUpdateLoop waiting for {SleepTime}ms with MaxCoreStuckTime set to {MaxCoreStuckTime}ms", sleepTime, maxCoreStuckTime);
            
            await Task.Delay((int)sleepTime);
        }
        
        await Update(TimeSpan.FromMilliseconds(diff));
        
        realPreviousTime = realCurrentTime;

        if (false)
        {
            // Increment the tick counter
            tickCount++;
    
            // Calculate TPS every second
            if ((DateTime.Now - lastTpsCalculationTime).TotalSeconds >= 1)
            {
                ticksPerSecond = tickCount / (DateTime.Now - lastTpsCalculationTime).TotalSeconds;
                // _logger.LogInformation("Ticks Per Second (TPS): {TPS}", ticksPerSecond);
                tickCount = 0;
                lastTpsCalculationTime = DateTime.Now;
            }
        }
    }
    
    private async Task Update(TimeSpan elapsedTime)
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
        var accountId = new AccountId(ulong.Parse(value.ToString()));
        
        // TODO: Send in game packet telling the player they are being disconnected
        Connections.FirstOrDefault(c => c.AccountId == accountId)?.Close();
    }
    #endregion
}
