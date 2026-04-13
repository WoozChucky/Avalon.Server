using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Tcp.Configuration;
using Microsoft.Extensions.Logging;

namespace Avalon.Network.Tcp;

[Obsolete("AvalonTcpServer is deprecated in favor ServerBase<T> implementations. This class will be removed in a future release.")]
public class AvalonTcpServer : IAvalonTcpServer
{
    protected readonly ILogger<AvalonTcpServer> Logger;
    protected readonly AvalonTcpServerConfiguration Configuration;
    protected readonly CancellationTokenSource Cts;
    protected readonly Socket Socket;
    protected volatile bool Running;
    protected readonly ILoggerFactory LoggerFactory;
    protected readonly ConcurrentDictionary<Guid, IAvalonTcpConnection> _connections = new();

    public event TcpClientConnectedHandler? ClientConnected;

    public AvalonTcpServer(ILoggerFactory loggerFactory, AvalonTcpServerConfiguration configuration, CancellationTokenSource cts)
    {
        LoggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        Logger = LoggerFactory.CreateLogger<AvalonTcpServer>();
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Cts = cts ?? throw new ArgumentNullException(nameof(cts));

        if (!Configuration.Enabled) return;

        Running = false;
        Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Socket.NoDelay = true;
        Socket.Bind(new IPEndPoint(IPAddress.Any, Configuration.ListenPort));
    }

    ~AvalonTcpServer()
    {
        Dispose(false);
    }

    public bool IsRunning => Running;

    public Task RunAsync()
    {
        if (!Configuration.Enabled) return Task.CompletedTask;

        if (Running)
        {
            throw new InvalidOperationException("Server is already running.");
        }

        Socket.Listen(Configuration.Backlog);
        Running = true;

        Logger.LogInformation("Listening at {EndPoint}", Socket.LocalEndPoint);

#pragma warning disable CS4014
        Task.Factory.StartNew(InternalServerLoop, Cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
#pragma warning restore CS4014
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!Configuration.Enabled) return;

        if (!Running) throw new InvalidOperationException("Server is not running.");

        Running = false;

        // Snapshot and clear before sending/closing so Disconnected callbacks don't mutate during iteration.
        var toClose = _connections.Values.ToArray();
        _connections.Clear();

        // Notify each client that the server is shutting down.
        var disconnectPacket = SDisconnectPacket.Create("Server is shutting down", DisconnectReason.ServerShutdown);
        foreach (var connection in toClose)
        {
            try
            {
                await connection.SendAsync(disconnectPacket);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Failed to send disconnect packet to connection {Id}", connection.Id);
            }
        }

        // Give the OS time to drain send buffers before forcibly closing sockets.
        await Task.Delay(200);

        foreach (var connection in toClose)
        {
            try
            {
                connection.Close();
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Failed to close connection {Id} during shutdown", connection.Id);
            }
        }

        Cts.Cancel();
        Socket.Close();

        Logger.LogInformation("Server stopped");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
        Logger.LogDebug("Disposed AvalonTcpServer");
    }

    private async Task InternalServerLoop()
    {
        try
        {
            while (!Cts.IsCancellationRequested)
            {
                var client = await Socket.AcceptAsync().ConfigureAwait(true);
                if (Socket == null) continue;
                await HandleNewConnection(client).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("Server loop cancelled");
        }
        catch (ObjectDisposedException)
        {
            Logger.LogDebug("Server socket closed; loop exiting");
        }
        catch (SocketException)
        {
            Logger.LogDebug("Server socket closed; loop exiting");
        }
    }

    protected virtual Task HandleNewConnection(Socket client)
    {
        try
        {
            var networkStream = new NetworkStream(client, true);
            var tcpClient = new TcpClient(LoggerFactory, client, networkStream);
            TrackConnection(tcpClient);
            ClientConnected?.Invoke(this, tcpClient);
        }
        catch (AuthenticationException e)
        {
            Logger.LogWarning(e, "Failed to authenticate: {Message}", e.Message);
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "Failed to handle new connection: {Message}", e.Message);
        }

        return Task.CompletedTask;
    }

    protected void TrackConnection(IAvalonTcpConnection connection)
    {
        connection.Disconnected += id => _connections.TryRemove(id, out _);
        _connections.TryAdd(connection.Id, connection);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Logger.LogTrace("Disposed Certificate");
            Socket.Dispose();
            Logger.LogTrace("Disposed Socket");
        }
    }
}
