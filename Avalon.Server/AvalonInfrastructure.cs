using System.Net.Sockets;
using System.Text;
using Avalon.Network;
using Avalon.Network.Packets;
using Microsoft.Extensions.Logging;
using ProtoBuf;
using TcpClient = Avalon.Network.TcpClient;

namespace Avalon.Server;

public class AvalonInfrastructure : IDisposable
{
    
    private readonly CancellationTokenSource _cts;
    private readonly ILogger<AvalonInfrastructure> _logger;
    private readonly AvalonGame _game;
    private readonly IAvalonUdpServer _udpServer;
    private readonly IAvalonTcpServer _tcpServer;

    public AvalonInfrastructure(
        ILogger<AvalonInfrastructure> logger,
        AvalonGame game,
        IAvalonTcpServer tcpServer, 
        IAvalonUdpServer udpServer
        )
    {
        _logger = logger;
        _game = game;
        _udpServer = udpServer;
        _tcpServer = tcpServer;
        _cts = new CancellationTokenSource();
        _tcpServer.ClientConnected += TcpServerOnClientConnected;
    }

    public async Task Run()
    {
        Task.Run(_game.RunAsync, _cts.Token);
        await _udpServer.RunAsync(false).ConfigureAwait(false);
        await _tcpServer.RunAsync(true).ConfigureAwait(false);
    }
    
    public async Task GracefulStop()
    {
        _cts.Cancel();
        await _udpServer.StopAsync().ConfigureAwait(false);
        await _tcpServer.StopAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing AvalonInfrastructure...");
        _cts.Dispose();
        _udpServer.Dispose();
        _tcpServer.Dispose();
        GC.SuppressFinalize(this);
    }

    private async void TcpServerOnClientConnected(object? sender, TcpClient client)
    {
        _logger.LogInformation("Client connected from {@EndPoint}", client.Socket.RemoteEndPoint);

        await Task.Run(async () =>
        {
            // Receive a response from the server
            var buffer = new byte[4096];
            var messageStream = new MemoryStream();

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var bytesRead = await client.Stream.ReadAsync(buffer, _cts.Token);
                    if (bytesRead == 0)
                    {
                        // Remote client disconnected
                        return;
                    }

                    // Write the received data to the memory stream
                    await messageStream.WriteAsync(buffer, _cts.Token);
                
                    // Check if the entire message has been received
                    var response = Encoding.UTF8.GetString(messageStream.ToArray());
                    var delimiterIndex = response.IndexOf("|exit", StringComparison.Ordinal);
                    while (delimiterIndex >= 0)
                    {
                        var message = response.Substring(0, delimiterIndex);
                        _logger.LogInformation("Received: {Response}", message);
                        
                        _game.AddMessage(message);
                        
                        messageStream = new MemoryStream(Encoding.UTF8.GetBytes(response.Substring(delimiterIndex + "|exit".Length)));
                        response = Encoding.UTF8.GetString(messageStream.ToArray());
                        delimiterIndex = response.IndexOf("|exit", StringComparison.Ordinal);
                    }


                    var uP = new UserPacket
                    {
                        Type = PacketType.DetailPacket
                    };

                    var uD = new UserDetails
                    {
                        Id = 10,
                        Active = true
                    };

                    using var ms = new MemoryStream();
                    //Serializer.SerializeWithLengthPrefix(ms, uP, PrefixStyle.Base128);
                    Serializer.Serialize(ms, uD);
                    uP.Content = ms.ToArray();
                    ms.Position = 0;
                    
                    Serializer.SerializeWithLengthPrefix(client.Stream, uP, PrefixStyle.Base128);
                    
                    //await client.Stream.WriteAsync(ms.ToArray(), _cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Operation cancelled");
            }
            catch (IOException e)
            {
                if (e.InnerException is SocketException && e.InnerException.Message.Contains("An existing connection was forcibly closed by the remote host"))
                {
                    _logger.LogInformation("Client disconnected");
                }
                else
                {
                    _logger.LogWarning(e, "IOException while reading from client stream");
                }
            }

            await client.Stream.DisposeAsync();

        }).ConfigureAwait(false);
    }
}
