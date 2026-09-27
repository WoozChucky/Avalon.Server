using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Hosting.PluginTypes;
using Avalon.Hosting.Telemetry;
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
    Task CallListener(IConnection connection, NetworkPacketHeader header, Packet? payload);
    long ServerTime { get; }
    int SendBufferCapacity { get; }
    void CallConnectionListener(IConnection connection);

    /// <summary>Which peers prefix their connections with a PROXY v2 header (#524).</summary>
    ProxyProtocolPolicy ProxyProtocol => ProxyProtocolPolicy.Disabled;
}

public class PacketHandlerCache
{
    public Func<IServiceProvider, object> HandlerFactory { get; set; }
}

public abstract class ServerBase<T> : BackgroundService, IServerBase where T : IConnection
{
    public ushort Port { get; }
    public int SendBufferCapacity { get; }
    public ProxyProtocolPolicy ProxyProtocol { get; }

    public PacketDispatchTelemetry PacketTelemetry { get; }

    /// <summary>TCP keepalive set on every accepted socket (#571).</summary>
    public TcpKeepAlive KeepAlive { get; }

    protected TcpListener Listener { get; }
    public IPacketManager PacketManager { get; }
    public readonly Dictionary<Type, PacketHandlerCache> HandlerCache = new();

    protected readonly ConcurrentDictionary<Guid, IConnection> Connections = new();
    private ImmutableArray<T> _typedConnections = ImmutableArray<T>.Empty;
    protected ImmutableArray<T> TypedConnections => _typedConnections;

    private readonly ILogger _logger;

    private readonly List<Func<IConnection, bool>> _connectionListeners = new();
    private readonly Stopwatch _serverTimer = new();
    private readonly CancellationTokenSource _stoppingToken = new();

    // Read once, while the source is alive (#578): its Token getter throws once StopAsync has
    // disposed the source, and a packet can still be dispatched then. The captured token stays
    // usable, and cancelled, after the dispose.
    private readonly CancellationToken _connectionsStopping;
    private readonly IServiceProvider _serviceProvider;

    // The accept loop (#578): cancelled first thing in StopAsync, before the listener stops.
    private readonly CancellationTokenSource _acceptStopping = new();
    private readonly Lock _acceptGate = new();
    private Task? _acceptLoop;
    private Task? _stopping;

    protected ServerBase(IPacketManager packetManager, ILogger logger,
        IServiceProvider serviceProvider, IOptions<HostingConfiguration> hostingOptions,
        PacketDispatchTelemetry? packetTelemetry = null)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _connectionsStopping = _stoppingToken.Token;
        PacketManager = packetManager;
        PacketTelemetry = packetTelemetry ?? PacketDispatchTelemetry.Disabled;
        Port = hostingOptions.Value.Port;
        SendBufferCapacity = hostingOptions.Value.SendBufferCapacity;
        ProxyProtocol = ProxyProtocolPolicy.From(hostingOptions.Value.ProxyProtocol);
        KeepAlive = new TcpKeepAlive(hostingOptions.Value, logger);

        // Start server timer
        _serverTimer.Start();

        var localAddr = IPAddress.Parse(hostingOptions.Value.Host);
        Listener = new TcpListener(localAddr, Port);
        Listener.Server.NoDelay = true;

        _logger.LogInformation("Initialize tcp server listening on {IP}:{Port}", localAddr, Port);
    }

    public long ServerTime => _serverTimer.ElapsedMilliseconds;
    public long ServerTicks => _serverTimer.ElapsedTicks;

    /// <summary>Who <paramref name="connection" /> is, for its packets' spans and log scope.</summary>
    protected virtual PacketTags DescribeConnection(IConnection connection) =>
        new(connection.Id,
            connection is Connection known ? known.ClientAddress : PacketTags.AddressOf(connection.RemoteEndPoint),
            null, null);

    protected abstract object GetContextPacket(IConnection connection, object? packet, Type packetType);
    protected abstract Task OnStoppingAsync(CancellationToken stoppingToken);

    /// <summary>Counterpart to <see cref="RemoveConnection"/>: both collections move together.</summary>
    protected void AddConnection(T connection)
    {
        if (Connections.TryAdd(connection.Id, connection))
            ImmutableInterlocked.Update(ref _typedConnections, static (arr, conn) => arr.Add(conn), connection);
    }

    public Task RemoveConnection(IConnection connection)
    {
        Connections.Remove(connection.Id, out _);
        if (connection is T typed)
            ImmutableInterlocked.Update(ref _typedConnections, static (arr, conn) => arr.Remove(conn), typed);
        return Task.CompletedTask;
    }

    public override Task StartAsync(CancellationToken token)
    {
        base.StartAsync(token);
        _logger.LogInformation("Start listening for connections...");

        StartListening();

        return Task.CompletedTask;
    }

    /// <summary>First wait after an accept failure that is not one peer's (#584); doubled each time.</summary>
    public static readonly TimeSpan AcceptBackoffStart = TimeSpan.FromMilliseconds(100);

    /// <summary>Longest wait between accepts that keep failing (#584).</summary>
    public static readonly TimeSpan AcceptBackoffCap = TimeSpan.FromSeconds(5);

    /// <summary>At most one accept-failure warning per window; the rest are counted into the next (#584).</summary>
    public static readonly TimeSpan AcceptFailureLogWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Accepts clients until the stop begins (#578). An awaited task, never an async void: a stop that
    /// lands mid-accept ends the loop quietly instead of throwing out of a callback, which ended the
    /// process. <see cref="StopAsync" /> cancels it and awaits it before anything else shuts down.
    /// Only a stop ends it (#584): a failed accept outside one is counted, logged at most once per
    /// <see cref="AcceptFailureLogWindow" />, and retried, at once when only that peer failed, and
    /// otherwise after a backoff from <see cref="AcceptBackoffStart" /> doubling to
    /// <see cref="AcceptBackoffCap" />, which the next accepted client resets.
    /// </summary>
    private async Task AcceptLoopAsync(CancellationToken acceptToken, CancellationToken connectionToken)
    {
        TimeSpan backoff = TimeSpan.Zero;
        long nextLogAt = 0;
        int unlogged = 0;

        while (!acceptToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await AcceptClientAsync(acceptToken).ConfigureAwait(false);
            }
            catch (Exception) when (acceptToken.IsCancellationRequested)
            {
                return; // The listener was stopped: a cancelled, aborted or refused accept all mean that.
            }
            catch (Exception e)
            {
                PacketTelemetry.RecordAcceptFailure(e);
                unlogged++;
                long now = Environment.TickCount64;
                if (now >= nextLogAt)
                {
                    _logger.LogWarning(e, "The listener failed to accept a client ({Failures} failures since the last warning); retrying",
                        unlogged);
                    unlogged = 0;
                    nextLogAt = now + (long)AcceptFailureLogWindow.TotalMilliseconds;
                }

                if (IsOnePeersFailure(e))
                    continue;

                backoff = backoff == TimeSpan.Zero ? AcceptBackoffStart : TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, AcceptBackoffCap.Ticks));
                try
                {
                    await DelayAcceptAsync(backoff, acceptToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (acceptToken.IsCancellationRequested)
                {
                    return;
                }

                continue;
            }

            backoff = TimeSpan.Zero;

            // A client accepted as the server stops is closed, not served.
            if (acceptToken.IsCancellationRequested)
            {
                client.Close();
                return;
            }

            // Runs synchronously up to the connection's first await, so the connection is registered
            // and started before the next accept, as with the callback it replaces.
            _ = ServeAsync(client, connectionToken);
        }
    }

    /// <summary>
    /// An accept that failed because its own peer reset or gave up (#584). Any other failure, running
    /// out of sockets or buffers above all, would fail again at once, so it is retried after a backoff.
    /// </summary>
    private static bool IsOnePeersFailure(Exception e) =>
        e is SocketException { SocketErrorCode: SocketError.ConnectionReset or SocketError.ConnectionAborted };

    /// <summary>Accepts one client from the listener; a seam for tests that inject accept failures (#584).</summary>
    protected virtual ValueTask<TcpClient> AcceptClientAsync(CancellationToken token) =>
        Listener.AcceptTcpClientAsync(token);

    /// <summary>Waits out one accept backoff; a seam for tests that record the delays (#584).</summary>
    protected virtual Task DelayAcceptAsync(TimeSpan delay, CancellationToken token) =>
        Task.Delay(delay, token);

    private async Task ServeAsync(TcpClient client, CancellationToken connectionToken)
    {
        T? registered = default;
        try
        {
            // Here, not in the accept's try (#571, #584): its catches are about the listener, and a
            // peer that already reset can make these throw. That ends this client, never accepting.
            client.NoDelay = true;
            // An option the platform lacks is skipped and logged once, never thrown.
            KeepAlive.Apply(client.Client);

            // will dispose once connection finished executing (canceled or disconnect)
            await using var scope = _serviceProvider.CreateAsyncScope();

            // cannot inject tcp client here
            var connection = ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider, client, this);
            // Registered before it starts: the auth server's liveness sweep (#555) reads Connections.
            AddConnection(connection);
            registered = connection;

            await connection.StartAsync(connectionToken);
            await connection.ExecuteTask!.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // A connection that never ran would never remove itself.
            if (registered is not null)
                await RemoveConnection(registered).ConfigureAwait(false);
            client.Close();
            // Nothing awaits this task, so an exception left in it would go unobserved (#578).
            _logger.LogError(e, "Failed to serve an accepted client");
        }
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

    public async Task CallListener(IConnection connection, NetworkPacketHeader header, Packet? payload)
    {
        // A packet that arrives once the stop has begun is dropped (#578): the shutdown closes its
        // connection, and closing it here would race that close.
        if (_connectionsStopping.IsCancellationRequested)
        {
            _logger.LogDebug("Dropped packet {PacketType}: the server is stopping", header.Type);
            return;
        }

        if (!PacketManager.TryGetPacketInfo(header.Type, out var details) || details.PacketHandlerType is null)
        {
            _logger.LogWarning("Could not find a handler for packet {PacketType}", header.Type);
            return;
        }

        if (!HandlerCache.TryGetValue(details.PacketType, out var handlerCache))
        {
            var objectFactory = ActivatorUtilities.CreateFactory(details.PacketHandlerType, []);
            handlerCache = new PacketHandlerCache
            {
                HandlerFactory = sp => objectFactory(sp, null)
            };
            HandlerCache[details.PacketType] = handlerCache;
        }

        var context = GetContextPacket(connection, payload, details.PacketType);

        using PacketDispatch dispatch = PacketTelemetry.Begin(header.Type, DescribeConnection(connection), _logger);
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();

            var packetHandler = handlerCache.HandlerFactory(scope.ServiceProvider);
            await ((IPacketHandlerNew)packetHandler).ExecuteAsync(context, _connectionsStopping).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            dispatch.Fail(e);
            _logger.LogError(e, "Failed to execute packet handler for {PacketType}", header.Type);
            connection.Close();
        }
    }

    public void CallConnectionListener(IConnection connection)
    {
        foreach (var listener in _connectionListeners) listener(connection);
    }

    /// <summary>
    /// Starts the listener and the one accept loop; <see cref="StartAsync" /> calls it. Idempotent, and
    /// a no-op once a stop has begun, so a late caller cannot reopen the port (#578).
    /// </summary>
    protected void StartListening()
    {
        lock (_acceptGate)
        {
            if (_acceptLoop is not null || _acceptStopping.IsCancellationRequested)
                return;

            Listener.Start();
            _acceptLoop = AcceptLoopAsync(_acceptStopping.Token, _connectionsStopping);
        }
    }

    /// <summary>
    /// Stops accepting, then shuts the connections down. Idempotent (#578): a second call gets the
    /// first call's task rather than reaching sources the first has disposed.
    /// </summary>
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_acceptGate)
            return _stopping ??= StopCoreAsync(cancellationToken);
    }

    // Called under _acceptGate, and runs under it until its first await: a StartListening racing the
    // stop either starts its loop before this reads it, or sees the cancel and does nothing.
    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        _acceptStopping.Cancel();
        Task? acceptLoop = _acceptLoop;

        Listener.Stop();
        // The loop never faults. Awaited so every client it accepted is registered before the
        // shutdown closes Connections.
        if (acceptLoop is not null)
            await acceptLoop.ConfigureAwait(false);

        await OnStoppingAsync(cancellationToken);
        await _stoppingToken.CancelAsync();
        await base.StopAsync(cancellationToken);
        _stoppingToken.Dispose();
        // StartListening still reads IsCancellationRequested after this, which a disposed source allows.
        _acceptStopping.Dispose();
    }
}
