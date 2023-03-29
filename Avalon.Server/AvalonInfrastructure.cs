using System.Net.Sockets;
using System.Text;
using Avalon.Network;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Crypto;
using Avalon.Network.Packets.Deserialization;
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
    private readonly IPacketDeserializer _packetDeserializer;
    private readonly IAvalonTcpServer _tcpServer;

    public AvalonInfrastructure(
        ILogger<AvalonInfrastructure> logger,
        AvalonGame game,
        IAvalonTcpServer tcpServer, 
        IAvalonUdpServer udpServer,
        IPacketDeserializer packetDeserializer
        )
    {
        _logger = logger;
        _game = game;
        _udpServer = udpServer;
        _packetDeserializer = packetDeserializer;
        _tcpServer = tcpServer;
        _cts = new CancellationTokenSource();
        _tcpServer.ClientConnected += TcpServerOnClientConnected;
    }

    public async Task Run()
    {
        _packetDeserializer.RegisterPacketDeserializers();
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
            var messageStream = new MemoryStream();

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    
                    messageStream.SetLength(0);
                    messageStream.Position = 0;

                    //var packet = Serializer.DeserializeWithLengthPrefix<NetworkPacket>(client.Stream, PrefixStyle.Base128);
                    var packet = await _packetDeserializer.DeserializeFromNetwork<NetworkPacket>(client.Stream);

                    switch (packet.Header.Type)
                    {
                        case NetworkPacketType.CMSG_REQUEST_ENCRYPTION_KEY:
                            
                            var encryptionKeyPacket = _packetDeserializer.Deserialize<CRequestCryptoKeyPacket>(
                                NetworkPacketType.CMSG_REQUEST_ENCRYPTION_KEY, 
                                packet.Payload
                            );
                            //textBox1.Text = Encoding.UTF8.GetString(encryptionKeyPacket.Key);
                            if (encryptionKeyPacket != null)
                            {
                                var sCryptoKeyPacket = new SCryptoKeyPacket
                                {
                                    Key = Encoding.UTF8.GetBytes("123456789")
                                };

                                Serializer.Serialize(messageStream, sCryptoKeyPacket);
                                
                                var resultingPacket = new NetworkPacket
                                {
                                    Header = new NetworkPacketHeader
                                    {
                                        Type = NetworkPacketType.SMSG_ENCRYPTION_KEY,
                                        Flags = NetworkPacketFlags.None,
                                        Version = 0
                                    },
                                    Payload = messageStream.ToArray()
                                };
                                
                                Serializer.SerializeWithLengthPrefix(client.Stream, resultingPacket, PrefixStyle.Base128);
                            }
                            break;
                    }
                    
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
                    _logger.LogInformation("Client {@Endpoint} disconnected", client.Socket.RemoteEndPoint);
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
