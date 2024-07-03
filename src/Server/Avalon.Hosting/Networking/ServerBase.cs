
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Hosting.PluginTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Hosting.Network;

public interface IServerBase
{
    Task RemoveConnection(IConnection connection);
    Task CallListener(IConnection connection, IPacketSerializable packet);
    long ServerTime { get; }
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
        IServiceProvider serviceProvider, IOptions<HostingOptions> hostingOptions)
    {
        _logger = logger;
        _pluginExecutor = pluginExecutor;
        _serviceProvider = serviceProvider;
        PacketManager = packetManager;
        Port = hostingOptions.Value.Port;

        // Start server timer
        _serverTimer.Start();

        var localAddr = IPAddress.Parse(hostingOptions.Value.IpAddress ?? "127.0.0.1");
        IpUtils.PublicIP = localAddr;
        Listener = new TcpListener(localAddr, Port);
        Listener.Server.NoDelay = true;
        
        _logger.LogInformation("Initialize tcp server listening on {IP}:{Port}", localAddr, Port);
    }

    public long ServerTime => _serverTimer.ElapsedMilliseconds;

    protected abstract object GetContextPacket(IConnection connection, object packet, Type packetType);

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
        await connection.ExecuteTask.ConfigureAwait(false);
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

    public async Task CallListener(IConnection connection, IPacketSerializable packet)
    {
        if (!PacketManager.TryGetPacketInfo(packet, out var details) || details.PacketHandlerType is null)
        {
            _logger.LogWarning("Could not find a handler for packet {PacketType}", packet.GetType());
            return;
        }

        object context = GetContextPacket(connection, packet, details.PacketType);

        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();

            var packetHandler = ActivatorUtilities.CreateInstance(scope.ServiceProvider, details.PacketHandlerType);
            var handlerExecuteMethod = details.PacketHandlerType.GetMethod("ExecuteAsync")!;
            await (Task) handlerExecuteMethod.Invoke(packetHandler, new[] {context, new CancellationToken()})!;
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

    private static object GetAuthContextPacket(IConnection connection, object packet, Type packetType)
    {
        // TODO caching
        var contextPacketProperty = typeof(AuthPacketContext<>).MakeGenericType(packetType)
            .GetProperty(nameof(AuthPacketContext<object>.Packet))!;
        var contextConnectionProperty = typeof(AuthPacketContext<>).MakeGenericType(packetType)
            .GetProperty(nameof(AuthPacketContext<object>.Connection))!;

        var context = Activator.CreateInstance(typeof(AuthPacketContext<>).MakeGenericType(packetType))!;
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

    public async override Task StopAsync(CancellationToken cancellationToken)
    {
        await _stoppingToken.CancelAsync();
        await base.StopAsync(cancellationToken);
        _stoppingToken.Dispose();
    }
}