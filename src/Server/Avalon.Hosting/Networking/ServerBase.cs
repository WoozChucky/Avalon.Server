using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Common.Cryptography;
using Avalon.Hosting.PluginTypes;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Avalon.Hosting.Networking;

public interface IServerBase
{
    Task RemoveConnection(IConnection connection);
    Task CallListener(IConnection connection, NetworkPacket packet, Packet? payload);
    long ServerTime { get; }
    public ICryptoManager Crypto { get; }
    void CallConnectionListener(IConnection connection);
}

public abstract class ServerBase<T> : BackgroundService, IServerBase where T : IConnection
{
    private readonly ILogger _logger;
    protected IPacketManager PacketManager { get; }
    private readonly List<Func<IConnection, bool>> _connectionListeners = new();
    protected readonly ConcurrentDictionary<Guid, IConnection> Connections = new();
    private readonly Stopwatch _serverTimer = new();
    private readonly CancellationTokenSource _stoppingToken = new();
    protected TcpListener Listener { get; }

    private readonly PluginExecutor _pluginExecutor;
    private readonly IServiceProvider _serviceProvider;
    public int Port { get; }

    public ServerBase(IPacketManager packetManager, ILogger logger, PluginExecutor pluginExecutor,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _pluginExecutor = pluginExecutor;
        _serviceProvider = serviceProvider;
        PacketManager = packetManager;
        Port = 21000; // TODO: Get from configuration
        Crypto = new CryptoManager();

        // Start server timer
        _serverTimer.Start();

        var localAddr = IPAddress.Parse("0.0.0.0"); // TODO: Get from configuration
        Listener = new TcpListener(localAddr, Port);
        Listener.Server.NoDelay = true;
        
        _logger.LogInformation("Initialize tcp server listening on {IP}:{Port}", localAddr, Port);
    }

    public long ServerTime => _serverTimer.ElapsedMilliseconds;
    public ICryptoManager Crypto { get; }

    protected abstract object GetContextPacket(IConnection connection, object? packet, Type packetType);

    public async Task RemoveConnection(IConnection connection)
    {
        Connections.Remove(connection.Id, out _);

        await _pluginExecutor.ExecutePlugins<IConnectionLifetimeListener>(x => x.OnDisconnectedAsync(_stoppingToken.Token));
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

        await _pluginExecutor.ExecutePlugins<IConnectionLifetimeListener>(x => x.OnConnectedAsync(_stoppingToken.Token));

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

        var context = GetContextPacket(connection, payload, details.PacketType);

        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();

            var packetHandler = ActivatorUtilities.CreateInstance(scope.ServiceProvider, details.PacketHandlerType);
            var handlerExecuteMethod = details.PacketHandlerType.GetMethod("ExecuteAsync")!;
            await ((Task) handlerExecuteMethod.Invoke(packetHandler, new[] {context, _stoppingToken.Token})!).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to execute packet handler");
            connection.Close();
        }
    }

    private static object GetWorldContextPacket(IConnection connection, object packet, Type packetType)
    {
        // TODO caching
        var contextPacketProperty = typeof(WorldPacketContext<>).MakeGenericType(packetType)
            .GetProperty(nameof(WorldPacketContext<object>.Packet))!;
        var contextConnectionProperty = typeof(WorldPacketContext<>).MakeGenericType(packetType)
            .GetProperty(nameof(WorldPacketContext<object>.Connection))!;

        var context = Activator.CreateInstance(typeof(WorldPacketContext<>).MakeGenericType(packetType))!;
        contextPacketProperty.SetValue(context, packet);
        contextConnectionProperty.SetValue(context, connection);
        return context;
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
        await _stoppingToken.CancelAsync();
        await base.StopAsync(cancellationToken);
        _stoppingToken.Dispose();
    }
}
