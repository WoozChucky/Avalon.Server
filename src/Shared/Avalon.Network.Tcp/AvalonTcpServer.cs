using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Avalon.Network.Tcp.Configuration;
using Microsoft.Extensions.Logging;

namespace Avalon.Network.Tcp;

public class AvalonTcpServer : IAvalonTcpServer
{
    protected readonly ILogger<AvalonTcpServer> Logger;
    protected readonly AvalonTcpServerConfiguration Configuration;
    protected readonly CancellationTokenSource Cts;
    protected readonly Socket Socket;
    protected volatile bool Running;
    protected readonly ILoggerFactory LoggerFactory;
    
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
    
    public async Task RunAsync()
    {
        if (!Configuration.Enabled) return;
        
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
    }

    public Task StopAsync()
    {
        if (!Configuration.Enabled) return Task.CompletedTask;
        
        if (!Running) throw new InvalidOperationException("Server is not running.");

        Running = false;

        //TODO(Nuno): Close all connections.
        //TODO(Nuno): Send the connection disconnect packet to all clients.
        Logger.LogInformation("Server stopped");
        
        return Task.CompletedTask;
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
    }

    protected virtual async Task HandleNewConnection(Socket client)
    {
        try
        {
            var networkStream = new NetworkStream(client, true);
            
            ClientConnected?.Invoke(this, new TcpClient(LoggerFactory, client, networkStream));
        }
        catch (AuthenticationException e)
        {
            Logger.LogWarning(e, "Failed to authenticate: {Message}", e.Message);
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "Failed to handle new connection: {Message}", e.Message);
        }
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
