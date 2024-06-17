using System.Diagnostics;
using System.Net.Sockets;
using Avalon.Auth;
using Avalon.Common.Telemetry;
using Avalon.Common.Threading;
using Avalon.Network;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Handshake;
using Avalon.Network.Packets.Internal;
using Avalon.Network.Packets.Internal.Deserialization;
using Avalon.Network.Packets.Internal.Exceptions;
using Avalon.Network.Packets.Serialization;
using Microsoft.Extensions.Logging;
using TcpClient = Avalon.Network.TcpClient;

namespace Avalon.Infrastructure.Auth;

public class AvalonAuthNetworkDaemon : IAvalonNetworkDaemon
{
    private readonly ILogger<AvalonAuthNetworkDaemon> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly IAvalonTcpServer _tcpServer;
    private readonly IPacketDeserializer _packetDeserializer;
    private readonly IPacketSerializer _packetSerializer;
    private readonly IPacketRegistry _packetRegistry;
    private readonly IAvalonAuth _authServer;
    private readonly IAuthSessionManager _sessionManager;

    private readonly RingBuffer<(IRemoteSource, NetworkPacket)> _packetProcessorBuffer;
    
    private long _bytesReceived;
    private long _packetsReceived;

    public AvalonAuthNetworkDaemon(
        ILoggerFactory loggerFactory,
        CancellationTokenSource cts,
        IAvalonTcpServer tcpServer,
        IPacketDeserializer packetDeserializer,
        IPacketSerializer packetSerializer,
        IPacketRegistry packetRegistry,
        IAvalonAuth authServer,
        IAuthSessionManager sessionManager)
    {
        _logger = loggerFactory.CreateLogger<AvalonAuthNetworkDaemon>();
        _cts = cts;
        _tcpServer = tcpServer;
        _packetDeserializer = packetDeserializer;
        _packetSerializer = packetSerializer;
        _packetRegistry = packetRegistry;
        _authServer = authServer;
        _sessionManager = sessionManager;

        _packetProcessorBuffer = new RingBuffer<(IRemoteSource, NetworkPacket)>("RECV",1024);

        _tcpServer = tcpServer;
        _tcpServer.ClientConnected += TcpServerOnClientConnected;
    }
    
    public void Start()
    {
        _packetSerializer.RegisterPacketSerializers();
        _packetDeserializer.RegisterPacketDeserializers();
        
        // Handshake handlers
        _packetRegistry.RegisterHandler<CRequestServerInfoPacket>(NetworkPacketType.CMSG_SERVER_INFO, _authServer.HandleServerInfoPacket);
        _packetRegistry.RegisterHandler<CClientInfoPacket>(NetworkPacketType.CMSG_CLIENT_INFO, _authServer.HandleClientInfoPacket);
        _packetRegistry.RegisterHandler<CHandshakePacket>(NetworkPacketType.CMSG_CLIENT_HANDSHAKE, _authServer.HandleHandshakePacket);
        
        // Auth handlers
        _packetRegistry.RegisterHandler<CAuthPacket>(NetworkPacketType.CMSG_AUTH, _authServer.HandleAuthPacket);
        _packetRegistry.RegisterHandler<CLogoutPacket>(NetworkPacketType.CMSG_LOGOUT, _authServer.HandleLogoutPacket);
        _packetRegistry.RegisterHandler<CRegisterPacket>(NetworkPacketType.CMSG_REGISTER, _authServer.HandleRegisterPacket);
        
        // World handlers
        _packetRegistry.RegisterHandler<CWorldListPacket>(NetworkPacketType.CMSG_WORLD_LIST, _authServer.HandleWorldListPacket);
        _packetRegistry.RegisterHandler<CWorldSelectPacket>(NetworkPacketType.CMSG_WORLD_SELECT, _authServer.HandleWorldSelectPacket);
        
        _logger.LogInformation("Starting network daemon");
        
        Task.Run(ProcessPacketsAsync).ConfigureAwait(false);
        Task.Run(_tcpServer.RunAsync).ConfigureAwait(false);
    }
    
    public void Stop()
    {
        _logger.LogInformation("Stopping network daemon");
        
        _tcpServer.StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
    }
    
    private void TcpServerOnClientConnected(object? sender, TcpClient client)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var packet = await _packetDeserializer.DeserializeFromNetwork<NetworkPacket>(client.Stream);

                    try
                    {
                        if (!client.Connected)
                        {
                            _sessionManager.RemoveConnection(client);
                            break;
                        }
                        
                        if (packet == null)
                        {
                            _logger.LogWarning("Client {Endpoint} connection was closed abruptly", client.RemoteAddress);
                            _sessionManager.RemoveConnection(client);
                            break;
                        }
 
                        _packetProcessorBuffer.Enqueue((client, packet));
                        
                        DiagnosticsConfig.Server.BytesReceived.Add(packet.Size);
                        DiagnosticsConfig.Server.PacketsReceived.Add(1, new KeyValuePair<string, object?>(
                            nameof(NetworkPacketType), packet.Header.Type
                        ));

                        Interlocked.Increment(ref _packetsReceived);
                        Interlocked.Add(ref _bytesReceived, packet.Size);
                    }
                    catch (PacketHandlerException e)
                    {
                        _logger.LogWarning(e, "Packet handler exception while handling packet {@Packet}", packet);
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(e, "Exception while handling packet {@Packet}", packet);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Operation cancelled");
            }
            catch (IOException e)
            {
                if (e.InnerException is SocketException &&
                    e.InnerException.Message.Contains("An existing connection was forcibly closed by the remote host"))
                {
                    _logger.LogInformation("Client {Endpoint} disconnected", client.RemoteAddress);
                    _sessionManager.RemoveConnection(client);
                }
                else
                {
                    _logger.LogWarning(e, "IOException while reading from client stream");
                }
            }
            finally
            {
                await client.Stream.DisposeAsync();
            }

        }).ConfigureAwait(false);
    }
    
    private async Task ProcessPacketsAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var (client, packet) = await _packetProcessorBuffer.DequeueAsync(_cts.Token);
                
                try
                {
                    var session = _sessionManager.GetSession(client);

                    var handler = _packetRegistry.GetHandler(packet.Header.Type);
                
                    var deserializedPacket = GetInnerPacket(packet, session);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var watch = Stopwatch.StartNew();
                        
                            using var activity = DiagnosticsConfig.Server.Source
                                .StartActivity("Packet Handler Loop", ActivityKind.Server);
                        
                            activity?.SetTag("network.packet.type", packet.Header.Type.ToString());
                            activity?.SetTag("network.packet.size", packet.Size);
                            activity?.SetTag("network.client.address", client.RemoteAddress);
                            activity?.SetTag("network.client.rtt", client.RoundTripTime);
                        
                            await handler.Handle(client, deserializedPacket).ConfigureAwait(false);
                        
                            watch.Stop();
                            var elapsedMs = watch.ElapsedMilliseconds;
                            if (elapsedMs > 0)
                                _logger.LogInformation("Packet {HeaderType} processing took {ElapsedMs} ms", packet.Header.Type, elapsedMs);
                        }
                        catch (Exception ex)
                        {
                            // Log the error, don't let it crash your process.
                            _logger.LogError(ex, "Error processing packet");
                        }
                    });
                }
                catch (PacketHandlerException e)
                {
                    _logger.LogWarning(e, "Packet handler exception while handling packet");
                    if (e.Message.StartsWith("Encrypted", StringComparison.InvariantCultureIgnoreCase))
                    {
                        _logger.LogWarning("Disconnecting client {Endpoint} due to invalid session key", client.RemoteAddress);
                        _sessionManager.RemoveConnection(client);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error processing packet");
        }
    }
    
    private Packet GetInnerPacket(NetworkPacket packet, AvalonAuthSession? session)
    {
        if (packet.Header.Flags == NetworkPacketFlags.Encrypted && session == null)
            throw new PacketHandlerException("Encrypted packet received without session key");
        
        DecryptFunc? decryptFunc = null;
        if (packet.Header.Flags == NetworkPacketFlags.Encrypted)
            decryptFunc = session!.Decrypt;
        
        return packet.Header.Type switch
        {
            NetworkPacketType.CMSG_SERVER_INFO => _packetDeserializer.Deserialize<CRequestServerInfoPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.CMSG_CLIENT_INFO => _packetDeserializer.Deserialize<CClientInfoPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.CMSG_CLIENT_HANDSHAKE => _packetDeserializer.Deserialize<CHandshakePacket>(packet.Header.Type, packet.Payload, decryptFunc),
            
            NetworkPacketType.CMSG_AUTH => _packetDeserializer.Deserialize<CAuthPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.CMSG_AUTH_PATCH => _packetDeserializer.Deserialize<CAuthPatchPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.CMSG_LOGOUT => _packetDeserializer.Deserialize<CLogoutPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.CMSG_REGISTER => _packetDeserializer.Deserialize<CRegisterPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            
            NetworkPacketType.CMSG_WORLD_LIST => _packetDeserializer.Deserialize<CWorldListPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.CMSG_WORLD_SELECT => _packetDeserializer.Deserialize<CWorldSelectPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            
            NetworkPacketType.CMSG_PING => _packetDeserializer.Deserialize<CPingPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.CMSG_PONG => _packetDeserializer.Deserialize<CPongPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            _ => throw new PacketHandlerException("Unknown packet type " + packet.Header.Type)
        };
    }
    
    public void Dispose()
    {
        _sessionManager.Dispose();
        _tcpServer.Dispose();
        
        GC.SuppressFinalize(this);
    }
}
