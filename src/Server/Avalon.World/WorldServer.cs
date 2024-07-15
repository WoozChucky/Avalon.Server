using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Character.Repositories;
using Avalon.Hosting;
using Avalon.Hosting.Networking;
using Avalon.Hosting.PluginTypes;
using Avalon.Infrastructure;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
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

public class CharacterListHandler(ICharacterRepository characterRepository) : WorldPacketHandler<CCharacterListPacket>
{
    
    public override void Execute(WorldConnection connection, CCharacterListPacket packet)
    {
        characterRepository.FindAllAsync();
        Console.WriteLine("CharacterListHandler");
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

        var a = ActivatorUtilities.CreateInstance<CharacterListHandler>(serviceProvider);
        
        PacketHandlers = new Dictionary<NetworkPacketType, IWorldPacketHandler>()
        {
            {NetworkPacketType.CMSG_CHARACTER_LIST, a},
        };
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
        
        _scriptHotReloader.ScriptsHotReloaded += OnScriptsHotReloaded;
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
            
            await Update(TimeSpan.FromMilliseconds(_targetElapsedTime.TotalMilliseconds));
        }

        // todo detect lags
    }
    
    private async Task Update(TimeSpan elapsedTime)
    {

        foreach (var worldConnection in Connections)
        {
            var filter = new WorldSessionFilter(worldConnection);

            worldConnection.Update(elapsedTime, filter);
        }
        
        await _world.Update(elapsedTime);
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
    
    private void OnScriptsHotReloaded(List<Type> types)
    {
        World.OnScriptsHotReload(types);
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
