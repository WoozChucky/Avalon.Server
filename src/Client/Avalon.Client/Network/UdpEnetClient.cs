using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Network;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Deserialization;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using ENet;
using ProtoBuf;
using Packet = ENet.Packet;

namespace Avalon.Client.Network;

public class UdpEnetClient : IDisposable
{
    
    private static UdpEnetClient instance;
    public static UdpEnetClient Instance => instance ??= new UdpEnetClient();
    
    
    public event PlayerConnectedHandler PlayerConnected;
    public event PlayerDisconnectedHandler PlayerDisconnected;
    public event PlayerMovedHandler PlayerMoved;
    public event LatencyUpdatedHandler LatencyUpdated;
    public event NpcUpdatedHandler NpcUpdated;
    
    private readonly CancellationTokenSource cts = new CancellationTokenSource();

    private readonly IPacketDeserializer _packetDeserializer;
    private readonly IPacketSerializer _packetSerializer;
    
    private long lastTick;
    private long lastSequenceNumber = 0;

    private readonly Host _client;
    private readonly Address _address;
    
    private Peer _serverPeer;
    
    
    public UdpEnetClient()
    {
        _packetDeserializer = new NetworkPacketDeserializer();
        _packetSerializer = new NetworkPacketSerializer();
        
        _client = new Host();
        _address = new Address();
        _address.SetIP("85.246.128.207");
        _address.Port = 21000;
        
        _client.Create();

        _packetDeserializer.RegisterPacketDeserializers();
        _packetSerializer.RegisterPacketSerializers();
        
        lastTick = DateTime.UtcNow.Ticks;
    }

    public async Task ConnectAsync()
    {
        _serverPeer = _client.Connect(_address);

        Task.Run(HandleCommunications);

        Task.Run(HandleLatency);
    }

    public async Task SendWelcomePacket()
    {
        using var buffer = new MemoryStream();
            
        var packet = CWelcomePacket.Create(Globals.ClientId);

        await SendToServer(packet);
    }

    public async Task BroadcastMovementUpdates(float time, float x, float y, float velX, float velY)
    {
        try
        {
            await using var buffer = new MemoryStream();

            if (velX == 0 && velY == 0)
            {
                //Console.WriteLine("No movement detected, skipping...");
            }
            
            var packet = CPlayerMovementPacket.Create(Globals.ClientId, time, x, y, velX, velY);

            await SendToServer(packet);
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
                LatencyUpdated?.Invoke(this, _serverPeer.RoundTripTime);
                
                Console.WriteLine("Last Receive Time: " + DateTime.UtcNow.AddMilliseconds(_serverPeer.LastReceiveTime).ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("Packets Lost: " + _serverPeer.PacketsLost);
                Console.WriteLine("Packet Sent: " + _serverPeer.PacketsSent);

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
            Event netEvent;
                    
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    bool polled = false;
                    
                    while (!polled)
                    {
                        if (_client.CheckEvents(out netEvent) <= 0)
                        {
                            if (_client.Service(15, out netEvent) <= 0)
                                break;

                            polled = true;
                        }

                        switch (netEvent.Type)
                        {
                            case EventType.None:
                                break;
                            case EventType.Connect:
                                Console.WriteLine("Client connected to server - ID: " + _serverPeer.ID);
                                break;
                            case EventType.Disconnect:
                                Console.WriteLine("Client disconnected from server");
                                break;
                            case EventType.Timeout:
                                Console.WriteLine("Client connection timeout");
                                break;
                            case EventType.Receive:
                            {
                                //Console.WriteLine("Packet received from server - Channel ID: " + netEvent.ChannelID +
                                //                  ", Data length: " + netEvent.Packet.Length);

                                var buffer = new byte[netEvent.Packet.Length];
                                netEvent.Packet.CopyTo(buffer);
                                
                                await using var ms = new MemoryStream(buffer);
                        
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

                                        // var sequenceNumber = Interlocked.Read(ref lastSequenceNumber);

                                        var rtt = (DateTime.UtcNow.Ticks - pongPacket.Ticks) / 10000;
                                            
                                        try {
                                            LatencyUpdated?.Invoke(this, rtt);
                                        } catch (Exception e) {
                                            Console.WriteLine(e);
                                        }
                                        break;
                                    }
                                    case NetworkPacketType.SMSG_PING:
                                    {
                                        var pingPacket = _packetDeserializer.Deserialize<SPingPacket>(packet.Header.Type,
                                            packet.Payload);
                                        
                                        var responsePacket = CPongPacket.Create(pingPacket.SequenceNumber, Globals.ClientId, pingPacket.Ticks);
                                        
                                        await SendToServer(responsePacket);
                                        
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
                                
                                netEvent.Packet.Dispose();
                                break;
                            }
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

    private async Task SendToServer(NetworkPacket packet)
    {
        await using var buffer = new MemoryStream();
        
        await _packetSerializer.SerializeToNetwork(buffer, packet);
                    
        var p = new Packet();
        p.Create(buffer.ToArray(), PacketFlags.Reliable);

        while (_serverPeer.State != PeerState.Connected)
        {
            await Task.Delay(100);
            Console.WriteLine("Waiting for connection to server to be established...");
        }
                    
        if (!_serverPeer.Send(0, ref p))
        {
            Console.WriteLine("Failed to send packet");
        }
    }
    
    public void Dispose()
    {
        cts?.Dispose();
        _client?.Dispose();
    }

    public void Disconnect()
    {
        _serverPeer.DisconnectNow(0);
    }
}
