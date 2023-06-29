using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Network;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Deserialization;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Client.Network;

public class UdpClient : IDisposable
{
    
    private static UdpClient _instance;
    public static UdpClient Instance => _instance ??= new UdpClient();
    
    public event PlayerMovedHandler PlayerMoved;
    public event LatencyUpdatedHandler LatencyUpdated;
    public event NpcUpdatedHandler NpcUpdated;
    
    private readonly CancellationTokenSource _cts;
    private readonly Socket _socket;
    
    private readonly IPEndPoint _serverEndpoint;
    
    private readonly IPacketDeserializer _packetDeserializer;
    private readonly IPacketSerializer _packetSerializer;
    
    private long _lastTick;
    private long _lastSequenceNumber;

    private UdpClient()
    {
        _cts = new CancellationTokenSource();
        _packetDeserializer = new NetworkPacketDeserializer();
        _packetSerializer = new NetworkPacketSerializer();
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _serverEndpoint = new IPEndPoint(IPAddress.Parse("85.246.128.207"), 21000);
        
        _packetDeserializer.RegisterPacketDeserializers();
        _packetSerializer.RegisterPacketSerializers();
        
        _lastTick = DateTime.UtcNow.Ticks;
    }

    public Task ConnectAsync()
    {
        var localEndpoint = new IPEndPoint(NetworkAdapters.GetLocalIpAddress(), 0); // 0 for random port
        _socket.Bind(localEndpoint);

        Task.Run(HandleLatency);
        Task.Run(HandleCommunications);
        
        return Task.CompletedTask;
    }

    public async Task SendWelcomePacket()
    {
        using var buffer = new MemoryStream();
            
        var packet = CWelcomePacket.Create(Globals.ClientId);
        
        await _packetSerializer.SerializeToNetwork(buffer, packet);

        await _socket.SendToAsync(buffer.ToArray(), SocketFlags.None, _serverEndpoint);
    }

    public async Task BroadcastMovementUpdates(float time, float x, float y, float velX, float velY)
    {
        try
        {
            await using var buffer = new MemoryStream();
            
            var packet = CPlayerMovementPacket.Create(Globals.ClientId, time, x, y, velX, velY);
            
            await _packetSerializer.SerializeToNetwork(buffer, packet);
            
            await _socket.SendToAsync(buffer.ToArray(), SocketFlags.None, _serverEndpoint);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    
    private async Task HandleLatency()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await using var buffer = new MemoryStream();
            
                    _lastTick = DateTime.UtcNow.Ticks;
                    var sequenceNumber = Interlocked.Increment(ref _lastSequenceNumber);

                    var packet = CPingPacket.Create(sequenceNumber, _lastTick);
            
                    await _packetSerializer.SerializeToNetwork(buffer, packet);
            
                    await _socket.SendToAsync(buffer.ToArray(), SocketFlags.None, _serverEndpoint);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to send ping packet. " + e);
                }

                await Task.Delay(1000);
            }
        }
        catch (OperationCanceledException e)
        {
            Trace.WriteLine(e);
        }
    }
    
    private async Task HandleCommunications()
    {
        try
        {
            
            var endpoint = new IPEndPoint(IPAddress.Any, 0);
                    
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var readBuffer = new byte[1024];
                    
                    var result = await _socket.ReceiveFromAsync(readBuffer, SocketFlags.None, endpoint);
                    var packetBuffer = new byte[result.ReceivedBytes];
                
                    Array.Copy(readBuffer, packetBuffer, result.ReceivedBytes);
                
                    await using var ms = new MemoryStream(packetBuffer);
                        
                    var packet = await _packetDeserializer.DeserializeFromNetwork<NetworkPacket>(ms);

                    switch (packet.Header.Type)
                    {
                        case NetworkPacketType.SMSG_PLAYER_POSITION_UPDATE:
                        {
                            var movementPacket = _packetDeserializer.Deserialize<SPlayerPositionUpdatePacket>(packet.Header.Type,
                                packet.Payload);
                            
                            try {
                                PlayerMoved?.Invoke(this, movementPacket);
                            } catch (Exception e) {
                                Console.WriteLine(e);
                            }
                            break;
                        } 
                        case NetworkPacketType.SMSG_PONG:
                        {
                            var pongPacket = _packetDeserializer.Deserialize<SPongPacket>(packet.Header.Type,
                                packet.Payload);

                            var sequenceNumber = Interlocked.Read(ref _lastSequenceNumber);

                            if (pongPacket.SequenceNumber == sequenceNumber)
                            {
                                var rtt = (DateTime.UtcNow.Ticks - pongPacket.Ticks) / 10000;
                            
                                
                                try {
                                    LatencyUpdated?.Invoke(this, rtt);
                                } catch (Exception e) {
                                    Console.WriteLine(e);
                                }
                            }
                            break;
                        }
                        case NetworkPacketType.SMSG_PING:
                        {
                            var pingPacket = _packetDeserializer.Deserialize<SPingPacket>(packet.Header.Type,
                                packet.Payload);
                            
                            var responsePacket = CPongPacket.Create(pingPacket.SequenceNumber, Globals.ClientId, pingPacket.Ticks);
                            
                            await using var buffer = new MemoryStream();
                            
                            await _packetSerializer.SerializeToNetwork(buffer, responsePacket);
            
                            await _socket.SendToAsync(buffer.ToArray(), SocketFlags.None, _serverEndpoint);
                            
                            break;
                        }
                        case NetworkPacketType.SMSG_NPC_UPDATE:
                        {
                            var npcUpdatePacket = Serializer.Deserialize<SNpcUpdatePacket>(new MemoryStream(packet.Payload));
                            try {
                                NpcUpdated?.Invoke(this, npcUpdatePacket);
                            } catch (Exception e) {
                                Console.WriteLine(e);
                            }
                            break;
                        }
                            
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }
        catch (OperationCanceledException e)
        {
            Trace.WriteLine(e);
        }
    }
    
    public void Dispose()
    {
        _cts?.Dispose();
        _socket?.Dispose();
    }
}
