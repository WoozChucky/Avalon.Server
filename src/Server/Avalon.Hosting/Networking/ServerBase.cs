using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Hosting.PluginTypes;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Hosting.Networking;

public interface IServerBase
{
    Task RemoveConnection(IConnection connection);
    Task CallListener(IConnection connection, NetworkPacket packet, Packet? payload);
    long ServerTime { get; }
    public ICryptoManager Crypto { get; }
    void CallConnectionListener(IConnection connection);
}

public class PacketHandlerCache
{
    public MethodInfo ExecuteMethod { get; set; }
    public Func<IServiceProvider, object> HandlerFactory { get; set; }
}

public abstract class ServerBase<T> : BackgroundService, IServerBase where T : IConnection
{
    public ushort Port { get; }
    
    protected TcpListener Listener { get; }
    public IPacketManager PacketManager { get; }
    public readonly Dictionary<Type, PacketHandlerCache> HandlerCache = new();
    
    protected readonly ConcurrentDictionary<Guid, IConnection> Connections = new();
    
    private readonly ILogger _logger;
    
    private readonly List<Func<IConnection, bool>> _connectionListeners = new();
    private readonly Stopwatch _serverTimer = new();
    private readonly CancellationTokenSource _stoppingToken = new();
    private readonly IServiceProvider _serviceProvider;

    protected ServerBase(IPacketManager packetManager, ILogger logger,
        IServiceProvider serviceProvider, IOptions<HostingConfiguration> hostingOptions)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        PacketManager = packetManager;
        Port = hostingOptions.Value.Port;
        Crypto = new CryptoManager();

        // Start server timer
        _serverTimer.Start();

        var localAddr = IPAddress.Parse(hostingOptions.Value.Host);
        Listener = new TcpListener(localAddr, Port);
        Listener.Server.NoDelay = true;
        
        _logger.LogInformation("Initialize tcp server listening on {IP}:{Port}", localAddr, Port);
    }

    public long ServerTime => _serverTimer.ElapsedMilliseconds;
    public long ServerTicks => _serverTimer.ElapsedTicks;
    public ICryptoManager Crypto { get; }

    protected abstract object GetContextPacket(IConnection connection, object? packet, Type packetType);
    protected abstract Task OnStoppingAsync(CancellationToken stoppingToken);

    public async Task RemoveConnection(IConnection connection)
    {
        Connections.Remove(connection.Id, out _);
    }
    
    public override Task StartAsync(CancellationToken token)
    {
        base.StartAsync(token);
        _logger.LogInformation("Start listening for connections...");

        Listener.Start();
        Listener.BeginAcceptTcpClient(OnClientAccepted, Listener);

        return Task.CompletedTask;
    }

    private async void OnClientAccepted(IAsyncResult ar)
    {
        var listener = (TcpListener) ar.AsyncState!;
        var client = listener.EndAcceptTcpClient(ar);

        // will dispose once connection finished executing (canceled or disconnect)
        await using var scope = _serviceProvider.CreateAsyncScope();

        // cannot inject tcp client here
        var connection = ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider, client, this);
        Connections.TryAdd(connection.Id, connection);

        // accept new connections on another thread
        Listener.BeginAcceptTcpClient(OnClientAccepted, Listener);

        await connection.StartAsync(_stoppingToken.Token);
        await connection.ExecuteTask!.ConfigureAwait(false);
    }

    public void ForAllConnections(Action<IConnection> callback)
    {
        foreach (var (_, connection) in Connections)
        {
            callback(connection);
        }
    }

    public void RegisterNewConnectionListener(Func<IConnection, bool> listener)
    {
        _connectionListeners.Add(listener);
    }

    public async Task CallListener(IConnection connection, NetworkPacket packet, Packet? payload)
    {
        if (!PacketManager.TryGetPacketInfo(packet, out var details) || details.PacketHandlerType is null)
        {
            _logger.LogWarning("Could not find a handler for packet {PacketType}", packet.Header.Type);
            return;
        }
        
        if (!HandlerCache.TryGetValue(details.PacketType, out var handlerCache))
        {
            // Cache reflection information
            var handlerExecuteMethod = details.PacketHandlerType.GetMethod("ExecuteAsync")
                                       ?? throw new InvalidOperationException($"Method 'ExecuteAsync' not found in {details.PacketHandlerType}");

            // Create factory delegate for packet handler instances
            var objectFactory = ActivatorUtilities.CreateFactory(details.PacketHandlerType, []);
            
            // Wrap ObjectFactory in a Func<IServiceProvider, object>
            object HandlerFactory(IServiceProvider sp) => objectFactory(sp, null);

            handlerCache = new PacketHandlerCache
            {
                ExecuteMethod = handlerExecuteMethod,
                HandlerFactory = HandlerFactory
            };

            HandlerCache[details.PacketType] = handlerCache;
        }
        
        var context = GetContextPacket(connection, payload, details.PacketType);

        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();

            var packetHandler = handlerCache.HandlerFactory(scope.ServiceProvider);
            await ((Task) handlerCache.ExecuteMethod.Invoke(packetHandler, new[] {context, _stoppingToken.Token})!).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to execute packet handler");
            connection.Close();
        }
    }

    public void CallConnectionListener(IConnection connection)
    {
        foreach (var listener in _connectionListeners) listener(connection);
    }

    protected void StartListening()
    {
        Listener.Start();
        Listener.BeginAcceptTcpClient(OnClientAccepted, Listener);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await OnStoppingAsync(cancellationToken);
        await _stoppingToken.CancelAsync();
        await base.StopAsync(cancellationToken);
        _stoppingToken.Dispose();
    }
}
