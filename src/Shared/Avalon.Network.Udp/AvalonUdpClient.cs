using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Avalon.Common.Threading;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Internal.Deserialization;
using Avalon.Network.Packets.Internal.Exceptions;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using ENet;
using Packet = ENet.Packet;
using APacket = Avalon.Network.Packets.Packet;

namespace Avalon.Network.Udp;

public class AvalonUdpClientSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
}

public class AvalonUdpClient : IDisposable
{
    public event PlayerMovedHandler? PlayerMoved;
    public event LatencyUpdatedHandler? LatencyUpdated;
    public event NpcUpdatedHandler? NpcUpdated;
    public event AuthResultHandler? AuthResult;
    
    private readonly CancellationTokenSource _cts;
    private readonly IPacketDeserializer _packetDeserializer;
    private readonly IPacketSerializer _packetSerializer;
    private readonly SemaphoreSlim _sendLock;
    private readonly RingBuffer<APacket> _receivedPacketBuffer;
    private readonly RingBuffer<NetworkPacket> _sendPacketBuffer;
    private readonly Host _client;
    private readonly Address _address;
    
    private Peer _serverPeer;

    public int AccountId { get; set; }
    public int CharacterId { get; set; }
    
    public AvalonUdpClient(AvalonUdpClientSettings settings)
    {
        _cts = new CancellationTokenSource();
        _packetDeserializer = new NetworkPacketDeserializer();
        _packetSerializer = new NetworkPacketSerializer();
        
        _sendLock = new SemaphoreSlim(1, 1);
        _receivedPacketBuffer = new RingBuffer<APacket>("",100);
        _sendPacketBuffer = new RingBuffer<NetworkPacket>("",100);
        
        _client = new Host();
        _address = new Address();
        _address.SetIP(Dns.GetHostAddresses(settings.Host).ToList()
            .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)!.ToString());
        _address.Port = (ushort)settings.Port;
        
        //var clientAddress = new Address 2499460
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
        
        Task.Run(ProcessPacketsAsync);
        Task.Run(HandleLatency);
        Task.Run(ProcessReceivedPackets);
        Task.Run(HandleCommunications);

        return Task.CompletedTask;
    }
    
    public void Disconnect()
    {
        _serverPeer.DisconnectNow(0);
    }

    public async Task SendAuthPatchPacket(int accountId, byte[] publicKey)
    {
        await SendToServer(CAuthPatchPacket.Create(accountId, publicKey));
    }
    
    public async Task BroadcastMovementUpdates(float time, float x, float y, float velX, float velY)
    {
        if (velX == 0 && velY == 0)
        {
            Console.WriteLine("No movement detected, skipping...");
        }

        throw new NotImplementedException();

        // await SendToServer(CPlayerMovementPacket.Create(AccountId, CharacterId, time, x, y, velX, velY));
    }
    
    private async void ProcessReceivedPackets()
    {
        try
        {
            async void Send(Action action)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                });
            }
            
            while (!_cts.IsCancellationRequested)
            {
                var packet = await _receivedPacketBuffer.DequeueAsync(_cts.Token);

                if (packet is null)
                {
                    Console.WriteLine("Received null packet in packet processor thread");
                    continue;
                }

                switch (packet)
                {
                    case SPlayerPositionUpdatePacket p:
                        Send(() => PlayerMoved?.Invoke(this, p));
                        break;
                    case SNpcUpdatePacket p:
                        Send(() => NpcUpdated?.Invoke(this, p));
                        break;
                    case SAuthResultPacket p:
                        Send(() => AuthResult?.Invoke(this, p));
                        break;
                    case SPingPacket p:
                        Send(() =>
                        {
                            var pongPacket = CPongPacket.Create(p.SequenceNumber, AccountId, p.Ticks);
                            
                            SendToServer(pongPacket);
                        });
                        break;
                    
                }
                
            }
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
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

                                if (packet == null)
                                {
                                    Console.WriteLine("Received null packet in network thread");
                                    continue;
                                }
                                
                                var innerPacket = GetInnerPacket(packet);
                                
                                _receivedPacketBuffer.Enqueue(innerPacket);
                                
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
    
    private APacket GetInnerPacket(NetworkPacket packet)
    {
        return packet.Header.Type switch
        {
            // Auth
            NetworkPacketType.SMSG_AUTH_RESULT => _packetDeserializer.Deserialize<SAuthResultPacket>(packet.Header.Type, packet.Payload),
            
            // Account
            NetworkPacketType.SMSG_PLAYER_POSITION_UPDATE => _packetDeserializer.Deserialize<SPlayerPositionUpdatePacket>(packet.Header.Type, packet.Payload),
            
            // TODO: Refactor this packet type and packet name
            NetworkPacketType.SMSG_NPC_UPDATE => _packetDeserializer.Deserialize<SNpcUpdatePacket>(packet.Header.Type, packet.Payload),

            // Generic
            NetworkPacketType.SMSG_PING => _packetDeserializer.Deserialize<SPingPacket>(packet.Header.Type, packet.Payload),
            
            _ => throw new PacketHandlerException("Unknown packet type " + packet.Header.Type)
        };
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

    private async Task SendToServer(NetworkPacket packet)
    {
        _sendPacketBuffer.Enqueue(packet);
    }
    
    private async Task ProcessPacketsAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var packet = await _sendPacketBuffer.DequeueAsync(_cts.Token);

                if (packet is null)
                {
                    continue;
                }
                
                await SendQueuedPacketAsync(packet);
            }
        }
        catch (OperationCanceledException)
        {
            
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
    
    private async Task SendQueuedPacketAsync(NetworkPacket packet)
    {
        await using var buffer = new MemoryStream();
        
        await _packetSerializer.SerializeToNetwork(buffer, packet);
                    
        var p = new Packet();
        p.Create(buffer.ToArray(), PacketFlags.Reliable);

        while (_serverPeer.State != PeerState.Connected)
        {
            Console.WriteLine("Waiting for connection to server to be established...");
            _serverPeer = _client.Connect(_address);
            await Task.Delay(100);
        }
        await _sendLock.WaitAsync();

        try
        {
            if (!_serverPeer.Send(0, ref p))
            {
                Console.WriteLine("Failed to send packet");
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }
    
    public void Dispose()
    {
        _cts?.Dispose();
        _client?.Dispose();
    }
}
