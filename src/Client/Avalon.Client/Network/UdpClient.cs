using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Network;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Deserialization;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Client.Network;

public class UdpClient : IDisposable
{
    
    private static UdpClient instance;
    public static UdpClient Instance => instance ??= new UdpClient();
    
    
    public event PlayerConnectedHandler PlayerConnected;
    public event PlayerDisconnectedHandler PlayerDisconnected;
    public event PlayerMovedHandler PlayerMoved;
    public event LatencyUpdatedHandler LatencyUpdated;
    public event NpcUpdatedHandler NpcUpdated;
    
    private readonly CancellationTokenSource cts = new CancellationTokenSource();
    private readonly Socket socket;
    
    private IPEndPoint serverEndpoint;
    
    private readonly IPacketDeserializer _packetDeserializer;
    private readonly IPacketSerializer _packetSerializer;
    
    private long lastTick;
    private long lastSequenceNumber = 0;
    
    
    public UdpClient()
    {
        _packetDeserializer = new NetworkPacketDeserializer();
        _packetSerializer = new NetworkPacketSerializer();
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        serverEndpoint = new IPEndPoint(IPAddress.Parse("85.246.128.207"), 21000);
        
        _packetDeserializer.RegisterPacketDeserializers();
        _packetSerializer.RegisterPacketSerializers();
        
        lastTick = DateTime.UtcNow.Ticks;
    }

    public async Task ConnectAsync()
    {
        var localEndpoint = new IPEndPoint(NetworkAdapters.GetLocalIpAddress(), 0); // 0 for random port
        socket.Bind(localEndpoint);

        await SendWelcomePacket();

        Task.Run(HandleLatency);
        Task.Run(HandleCommunications);
    }

    private async Task SendWelcomePacket()
    {
        using var buffer = new MemoryStream();
            
        var packet = CWelcomePacket.Create(Globals.ClientId);
        
        await _packetSerializer.SerializeToNetwork(buffer, packet);

        await socket.SendToAsync(buffer.ToArray(), SocketFlags.None, serverEndpoint);
    }

    public async Task BroadcastMovementUpdates(float time, float x, float y, float velX, float velY)
    {
        try
        {
            await using var buffer = new MemoryStream();
            
            var packet = CPlayerMovementPacket.Create(Globals.ClientId, time, x, y, velX, velY);
            
            await _packetSerializer.SerializeToNetwork(buffer, packet);
            
            await socket.SendToAsync(buffer.ToArray(), SocketFlags.None, serverEndpoint);
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
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await using var buffer = new MemoryStream();
            
                    lastTick = DateTime.UtcNow.Ticks;
                    var sequenceNumber = Interlocked.Increment(ref lastSequenceNumber);

                    var packet = CPingPacket.Create(sequenceNumber, lastTick);
            
                    await _packetSerializer.SerializeToNetwork(buffer, packet);
            
                    await socket.SendToAsync(buffer.ToArray(), SocketFlags.None, serverEndpoint);
                }
                catch (Exception e)
                {
                   
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
                    
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var readBuffer = new byte[1024];
                    
                    var result = await socket.ReceiveFromAsync(readBuffer, SocketFlags.None, endpoint);
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
                            PlayerMoved?.Invoke(this, movementPacket);
                            break;
                        } 
                        case NetworkPacketType.SMSG_PONG:
                        {
                            var pongPacket = _packetDeserializer.Deserialize<SPongPacket>(packet.Header.Type,
                                packet.Payload);

                            var sequenceNumber = Interlocked.Read(ref lastSequenceNumber);

                            if (pongPacket.SequenceNumber == sequenceNumber)
                            {
                                var rtt = (DateTime.UtcNow.Ticks - pongPacket.Ticks) / 10000;
                            
                                LatencyUpdated?.Invoke(this, rtt);
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
            
                            await socket.SendToAsync(buffer.ToArray(), SocketFlags.None, serverEndpoint);
                            
                            break;
                        }
                        case NetworkPacketType.SMSG_NPC_UPDATE:
                        {
                            var npcUpdatePacket = Serializer.Deserialize<SNpcUpdatePacket>(new MemoryStream(packet.Payload));
                            NpcUpdated?.Invoke(this, npcUpdatePacket);
                            break;
                        }
                            
                    }
                }
                catch (Exception e)
                {
                   
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
        cts?.Dispose();
        socket?.Dispose();
    }
}
