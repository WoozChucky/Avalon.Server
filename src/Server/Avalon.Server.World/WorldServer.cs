using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using Avalon.Configuration;
using Avalon.Hosting;
using Avalon.Hosting.Networking;
using Avalon.Hosting.PluginTypes;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Maps;
using Avalon.World.Pools;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Server.World;

public interface IWorldServer
{
    ImmutableArray<IWorldConnection> Connections { get; }
    IWorld World { get; }
}

public class WorldServer : ServerBase<WorldConnection>, IWorldServer
{
    public new ImmutableArray<IWorldConnection> Connections =>
        [..base.Connections.Values.Cast<WorldConnection>()];

    public IWorld World => _world;

    private readonly ConcurrentDictionary<Type, (PropertyInfo packetProperty, PropertyInfo connectionProperty)> _propertyCache = new();
    private readonly ILogger<WorldServer> _logger;
    private readonly PluginExecutor _pluginExecutor;
    private readonly IAvalonMapManager _mapManager;
    private readonly IPoolManager _poolManager;
    private readonly IAiController _aiController;
    private readonly ICreatureSpawner _creatureSpawner;
    private readonly Avalon.World.World _world;

    #region Server Timers

    private readonly Stopwatch _gameTime = new Stopwatch();
    private long _previousTicks = 0;
    private TimeSpan _accumulatedElapsedTime;
    private readonly TimeSpan _targetElapsedTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60); // 60hz
    private readonly TimeSpan _maxElapsedTime = TimeSpan.FromMilliseconds(500);
    private readonly Stopwatch _serverTimer = new();

    #endregion
    

    public WorldServer(IPacketManager packetManager, ILoggerFactory loggerFactory, PluginExecutor pluginExecutor,
        IServiceProvider serviceProvider,
        IOptions<HostingConfiguration> hostingOptions, IWorld world, IAvalonMapManager mapManager, IPoolManager poolManager,
        IAiController aiController, ICreatureSpawner creatureSpawner)
        : base(packetManager, loggerFactory.CreateLogger<WorldServer>(), pluginExecutor, serviceProvider,
            hostingOptions)
    {
        _logger = loggerFactory.CreateLogger<WorldServer>();
        _pluginExecutor = pluginExecutor;
        _mapManager = mapManager;
        _poolManager = poolManager;
        _aiController = aiController;
        _creatureSpawner = creatureSpawner;
        _world = world as Avalon.World.World ?? throw new InvalidOperationException("Invalid world instance");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RegisterNewConnectionListener(NewConnection);

        {
            await Task.WhenAll(
                _aiController.LoadAsync(),
                _creatureSpawner.LoadAsync()
            );

            await _mapManager.LoadAsync();
            
            await _world.LoadAsync();
        }
        
        _serverTimer.Start();
        
        StartListening();
        
        _gameTime.Start();
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // await _pluginExecutor.ExecutePlugins<IGameTickListener>(x => x.PreUpdateAsync(stoppingToken));
                await Tick();
                // await _pluginExecutor.ExecutePlugins<IGameTickListener>(x => x.PostUpdateAsync(stoppingToken));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Tick failed");
            }
        }
        
        _logger.LogInformation("World server stopped... Ran for {Minutes}mins", (int) _serverTimer.Elapsed.TotalMinutes);
    }
    
    private async ValueTask Tick()
    {
        var currentTicks = _gameTime.Elapsed.Ticks;
        _accumulatedElapsedTime += TimeSpan.FromTicks(currentTicks - _previousTicks);
        _previousTicks = currentTicks;

        if (_accumulatedElapsedTime < _targetElapsedTime)
        {
            var sleepTime = (_targetElapsedTime - _accumulatedElapsedTime).TotalMilliseconds;
            await Task.Delay((int) sleepTime).ConfigureAwait(false);
            return;
        }

        if (_accumulatedElapsedTime > _maxElapsedTime)
        {
            _logger.LogWarning("Server is running slow. Accumulated time: {AccumulatedTime}ms", _accumulatedElapsedTime);
            _accumulatedElapsedTime = _maxElapsedTime;
        }
        
        while (_accumulatedElapsedTime >= _targetElapsedTime)
        {
            _accumulatedElapsedTime -= _targetElapsedTime;
            
            Update(_targetElapsedTime.TotalMilliseconds);
        }

        // todo detect lags
    }
    
    private void Update(double elapsedTime)
    {
        // Update the world ...
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
}
