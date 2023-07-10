using System.Diagnostics;
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

namespace Avalon.Network.Udp;

public class AvalonUdpClientSettings
{
    public string Host { get; set; }
    public int Port { get; set; }
}

public class AvalonUdpClient : IDisposable
{
    private readonly AvalonUdpClientSettings _settings;
    public event PlayerMovedHandler PlayerMoved;
    public event LatencyUpdatedHandler LatencyUpdated;
    public event NpcUpdatedHandler NpcUpdated;
    public event AuthResultHandler AuthResult;
    
    private readonly CancellationTokenSource _cts;
    private readonly IPacketDeserializer _packetDeserializer;
    private readonly IPacketSerializer _packetSerializer;
    private readonly Host _client;
    private readonly Address _address;
    
    private Peer _serverPeer;
    private byte[] _privateKey;

    public int AccountId { get; set; }
    public int CharacterId { get; set; }
    
    public AvalonUdpClient(AvalonUdpClientSettings settings)
    {
        _settings = settings;
        _cts = new CancellationTokenSource();
        _packetDeserializer = new NetworkPacketDeserializer();
        _packetSerializer = new NetworkPacketSerializer();
        
        _client = new Host();
        _address = new Address();
        _address.SetIP(_settings.Host);
        _address.Port = (ushort)_settings.Port;
        
        //var clientAddress = new Address
        //{
        //    Port = 21500
        //};
        //_client.Create(clientAddress, 1, 1);
        _client.Create();

        _packetDeserializer.RegisterPacketDeserializers();
        _packetSerializer.RegisterPacketSerializers();
    }

    public Task ConnectAsync()
    {
        _serverPeer = _client.Connect(_address);
        
        Task.Run(HandleCommunications);
        Task.Run(HandleLatency);

        return Task.CompletedTask;
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
            
            var packet = CPlayerMovementPacket.Create(AccountId, CharacterId, time, x, y, velX, velY);

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
            while (!_cts.IsCancellationRequested)
            {
                LatencyUpdated?.Invoke(this, _serverPeer.RoundTripTime);
                
                //Console.WriteLine("Last Receive Time: " + DateTime.UtcNow.AddMilliseconds(_serverPeer.LastReceiveTime).ToString(CultureInfo.InvariantCulture));
                //Console.WriteLine("Packets Lost: " + _serverPeer.PacketsLost);
                //Console.WriteLine("Packet Sent: " + _serverPeer.PacketsSent);

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
                    
            while (!_cts.IsCancellationRequested)
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
                                        
                                        var responsePacket = CPongPacket.Create(pingPacket.SequenceNumber, AccountId, pingPacket.Ticks);
                                        
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
                                    case NetworkPacketType.SMSG_AUTH_RESULT:
                                        var authResultPacket = Serializer.Deserialize<SAuthResultPacket>(new MemoryStream(packet.Payload));
                                        try {
                                            AuthResult?.Invoke(this, authResultPacket);
                                        } catch (Exception e) {
                                            Console.WriteLine(e);
                                        }
                                        break;
                                        
                                }
                                
                                netEvent.Packet.Dispose();
                                break;
                            }
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
            Console.WriteLine(e);
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
            _serverPeer.Reset();
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
        _cts?.Dispose();
        _client?.Dispose();
    }

    public void Disconnect()
    {
        _serverPeer.DisconnectNow(0);
    }

    public void SetPrivateKey(byte[] privateKey)
    {
        this._privateKey = privateKey;
    }

    public async Task SendAuthPatchPacket(int accountId)
    {
        await SendToServer(CAuthPatchPacket.Create(accountId, _privateKey));
    }
}
