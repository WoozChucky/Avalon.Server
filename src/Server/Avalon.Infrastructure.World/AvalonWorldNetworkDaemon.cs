using System.Diagnostics;
using System.Net.Sockets;
using Avalon.Common.Telemetry;
using Avalon.Common.Threading;
using Avalon.Game;
using Avalon.Metrics;
using Avalon.Network;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Audio;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Handshake;
using Avalon.Network.Packets.Internal;
using Avalon.Network.Packets.Internal.Deserialization;
using Avalon.Network.Packets.Internal.Exceptions;
using Avalon.Network.Packets.Map;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Quest;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using Avalon.Network.Packets.World;
using Microsoft.Extensions.Logging;
using TcpClient = Avalon.Network.TcpClient;

namespace Avalon.Infrastructure.World;


public class AvalonWorldNetworkDaemon : IAvalonNetworkDaemon
{
    private readonly ILogger<AvalonWorldNetworkDaemon> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly IAvalonTcpServer _tcpServer;
    private readonly IPacketDeserializer _packetDeserializer;
    private readonly IPacketSerializer _packetSerializer;
    private readonly IPacketRegistry _packetRegistry;
    private readonly IAvalonGame _game;
    private readonly IAvalonSessionManager _sessionManager;
    private readonly IMetricsManager _metrics;
    
    private readonly RingBuffer<(IRemoteSource, NetworkPacket)> _packetProcessorBuffer;
    
    private long _bytesReceived;
    private long _packetsReceived;

    public AvalonWorldNetworkDaemon(
        ILoggerFactory loggerFactory, 
        CancellationTokenSource cts,
        IAvalonTcpServer tcpServer,
        IPacketDeserializer packetDeserializer,
        IPacketSerializer packetSerializer,
        IPacketRegistry packetRegistry,
        IAvalonGame game,
        IAvalonSessionManager sessionManager,
        IMetricsManager metrics)
    {
        _logger = loggerFactory.CreateLogger<AvalonWorldNetworkDaemon>();
        _cts = cts;

        _packetDeserializer = packetDeserializer;
        _packetSerializer = packetSerializer;
        _packetRegistry = packetRegistry;
        _game = game;
        _sessionManager = sessionManager;
        _metrics = metrics;
        
        _packetProcessorBuffer = new RingBuffer<(IRemoteSource, NetworkPacket)>("RECV",1024);

        _tcpServer = tcpServer;
        _tcpServer.ClientConnected += TcpServerOnClientConnected;
    }

    public void Start()
    {
        _packetSerializer.RegisterPacketSerializers();
        _packetDeserializer.RegisterPacketDeserializers();
        
        // Handshake handlers
        _packetRegistry.RegisterHandler<CRequestServerInfoPacket>(NetworkPacketType.CMSG_SERVER_INFO, _game.HandleServerInfoPacket);
        _packetRegistry.RegisterHandler<CClientInfoPacket>(NetworkPacketType.CMSG_CLIENT_INFO, _game.HandleClientInfoPacket);
        _packetRegistry.RegisterHandler<CHandshakePacket>(NetworkPacketType.CMSG_CLIENT_HANDSHAKE, _game.HandleHandshakePacket);
        
        // Auth handlers
        _packetRegistry.RegisterHandler<CAuthPacket>(NetworkPacketType.CMSG_AUTH, _game.HandleAuthPacket);
        _packetRegistry.RegisterHandler<CLogoutPacket>(NetworkPacketType.CMSG_LOGOUT, _game.HandleLogoutPacket);
        _packetRegistry.RegisterHandler<CRegisterPacket>(NetworkPacketType.CMSG_REGISTER, _game.HandleRegisterPacket);
        
        // Audio handlers
        _packetRegistry.RegisterHandler<CAudioRecordPacket>(NetworkPacketType.CMSG_AUDIO_RECORD, _game.HandleAudioRecordPacket);
        
        // Character handlers
        _packetRegistry.RegisterHandler<CCharacterListPacket>(NetworkPacketType.CMSG_CHARACTER_LIST, _game.HandleCharacterListPacket);
        _packetRegistry.RegisterHandler<CCharacterSelectedPacket>(NetworkPacketType.CMSG_CHARACTER_SELECTED, _game.HandleCharacterSelectedPacket);
        _packetRegistry.RegisterHandler<CCharacterCreatePacket>(NetworkPacketType.CMSG_CHARACTER_CREATE, _game.HandleCharacterCreatePacket);
        _packetRegistry.RegisterHandler<CCharacterDeletePacket>(NetworkPacketType.CMSG_CHARACTER_DELETE, _game.HandleCharacterDeletePacket);
        _packetRegistry.RegisterHandler<CCharacterLoadedPacket>(NetworkPacketType.CMSG_CHARACTER_LOADED, _game.HandleCharacterLoadedPacket);
        
        // Map handlers
        _packetRegistry.RegisterHandler<CMapTeleportPacket>(NetworkPacketType.CMSG_MAP_TELEPORT, _game.HandleMapTeleportPacket);
        
        // World handlers
        _packetRegistry.RegisterHandler<CInteractPacket>(NetworkPacketType.CMSG_INTERACT, _game.HandleInteractPacket);
        
        // Movement handlers
        _packetRegistry.RegisterHandler<CPlayerMovementPacket>(NetworkPacketType.CMSG_MOVEMENT, _game.HandleMovementPacket);
        
        // Quest handlers
        _packetRegistry.RegisterHandler<CQuestStatusPacket>(NetworkPacketType.CMSG_QUEST_STATUS, _game.HandleQuestStatusPacket);
        _packetRegistry.RegisterHandler<CQuestStatusPacket>(NetworkPacketType.CMSG_QUEST_LIST, _game.HandleQuestListPacket);
        
        // Latency handlers
        _packetRegistry.RegisterHandler<CPingPacket>(NetworkPacketType.CMSG_PING, _game.HandlePingPacket);
        _packetRegistry.RegisterHandler<CPongPacket>(NetworkPacketType.CMSG_PONG, _sessionManager.HandlePongPacket);
        
        
        // Social handlers
        _packetRegistry.RegisterHandler<CChatMessagePacket>(NetworkPacketType.CMSG_CHAT_MESSAGE, _game.HandleChatMessagePacket);
        _packetRegistry.RegisterHandler<COpenChatPacket>(NetworkPacketType.CMSG_CHAT_OPEN, _game.HandleOpenChatPacket);
        _packetRegistry.RegisterHandler<CCloseChatPacket>(NetworkPacketType.CMSG_CHAT_CLOSE, _game.HandleCloseChatPacket);
        _packetRegistry.RegisterHandler<CGroupInviteResultPacket>(NetworkPacketType.CMSG_GROUP_INVITE_RESULT, _game.HandleGroupInviteResultPacket);

        _logger.LogInformation("Starting network daemon");
        
        Task.Run(ProcessPacketsAsync).ConfigureAwait(false);
        
        _sessionManager.Start();
        
        Task.Run(_tcpServer.RunAsync).ConfigureAwait(false);
        //Task.Run(UpdateMetricsAsync).ConfigureAwait(false);
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

    private async Task UpdateMetricsAsync()
    {
        long previousBytesReceived = 0;
        long previousPacketsReceived = 0;
        var lastUpdate = DateTime.UtcNow;
        
        while (!_cts.IsCancellationRequested)
        {
            await Task.Delay(5000, _cts.Token).ConfigureAwait(false);
            
            if (false)
            {
                var currentBytesReceived = Interlocked.Read(ref _bytesReceived);
                var currentPacketsReceived = Interlocked.Read(ref _packetsReceived);
                
                // Bytes conversion to Mb
                var kbBytesReceived = currentBytesReceived / 1024;
                var mgBytesReceived = currentBytesReceived / (1024 * 1024);
                var gbBytesReceived = currentBytesReceived / (1024 * 1024 * 1024);
                
                // Byte rate calculation
                var bytesInLastSecond = currentBytesReceived - previousBytesReceived;
                var byteRate = Math.Round(bytesInLastSecond / Math.Max((DateTime.UtcNow - lastUpdate).TotalSeconds, 1), 2);
                
                DiagnosticsConfig.Server.BytesReceivedRate.Record(byteRate);
                
                
                // Packet rate calculation
                var packetsInLastSecond = currentPacketsReceived - previousPacketsReceived;
                var packetRate = (packetsInLastSecond / Math.Max((DateTime.UtcNow - lastUpdate).TotalSeconds, 1));
                
                DiagnosticsConfig.Server.PacketReceivedRate.Record(packetRate);

                LogNetworkMetrics("Bytes received", kbBytesReceived, "Kb", "bytes", byteRate);
                LogNetworkMetrics("Packets received", currentPacketsReceived, "p", "packets", packetRate);
                //LogNetworkMetrics("(UDP) Bytes", kbBytesReceivedUdp, "Kb", null, null);
                //LogNetworkMetrics("(UDP) Packets", Interlocked.Read(ref _udpPacketsReceived), null, null, null);
                //LogNetworkMetrics("(TCP) Bytes", kbBytesReceivedTcp, "Kb", null, null);
                //LogNetworkMetrics("(TCP) Packets", Interlocked.Read(ref _tcpPacketsReceived), null, null, null);
                //_logger.LogInformation("---------------------------------");
                
                previousPacketsReceived = currentPacketsReceived;
                previousBytesReceived = currentBytesReceived;
                lastUpdate = DateTime.UtcNow;
            }
            
            _metrics.QueueMetric("network.bytes_received", Interlocked.Read(ref _bytesReceived));
            
            _metrics.QueueMetric("network.packets_received", Interlocked.Read(ref _packetsReceived));
        }
    }

    private void LogNetworkMetrics(string metricName, long value, string? unit, string? rateUnit, double? rate)
    {
        if (unit == null && rateUnit == null && rate == null)
            _logger.LogDebug("{0}: {1}", metricName, value);
        else if (unit != null && rateUnit == null && rate == null)
            _logger.LogDebug("{0}: {1} {2}", metricName, value, unit);
        else if (unit != null && rateUnit != null && rate != null)
            _logger.LogDebug("{0}: {1}{2}{3}", metricName, value, unit, rate.HasValue ? $", Rate: {rate} {rateUnit}/second" : null);
    }

    public async void Stop()
    {
        _logger.LogInformation("Stopping network daemon");
        
        _sessionManager.Stop();
        
        await _tcpServer.StopAsync().ConfigureAwait(false);
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
    
    private Packet GetInnerPacket(NetworkPacket packet, AvalonWorldSession? session)
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
            
            NetworkPacketType.CMSG_AUDIO_RECORD => _packetDeserializer.Deserialize<CAudioRecordPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            
            NetworkPacketType.CMSG_CHARACTER_LIST => _packetDeserializer.Deserialize<CCharacterListPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.CMSG_CHARACTER_SELECTED => _packetDeserializer.Deserialize<CCharacterSelectedPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.CMSG_CHARACTER_CREATE => _packetDeserializer.Deserialize<CCharacterCreatePacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.CMSG_CHARACTER_DELETE => _packetDeserializer.Deserialize<CCharacterDeletePacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.CMSG_CHARACTER_LOADED => _packetDeserializer.Deserialize<CCharacterLoadedPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            
            NetworkPacketType.CMSG_MAP_TELEPORT => _packetDeserializer.Deserialize<CMapTeleportPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            
            NetworkPacketType.CMSG_INTERACT => _packetDeserializer.Deserialize<CInteractPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            
            NetworkPacketType.CMSG_MOVEMENT => _packetDeserializer.Deserialize<CPlayerMovementPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            
            NetworkPacketType.CMSG_PING => _packetDeserializer.Deserialize<CPingPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.CMSG_PONG => _packetDeserializer.Deserialize<CPongPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.CMSG_CHAT_MESSAGE => _packetDeserializer.Deserialize<CChatMessagePacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.CMSG_CHAT_OPEN => _packetDeserializer.Deserialize<COpenChatPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.CMSG_CHAT_CLOSE => _packetDeserializer.Deserialize<CCloseChatPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.CMSG_GROUP_INVITE_RESULT => _packetDeserializer.Deserialize<CGroupInviteResultPacket>(packet.Header.Type, packet.Payload, decryptFunc),
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
