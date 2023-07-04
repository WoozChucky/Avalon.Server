using System.Collections.Concurrent;
using Avalon.Game.Entities;
using Avalon.Network;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Character = Avalon.Database.Characters.Character;

namespace Avalon.Game;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    TimedOut,
    PendingKey
}

public class AvalonSession : IDisposable
{
    public int AccountId { get; private set; }
    public byte[] SessionKey { get; private set; }
    public Character? Character { get; set; }
    public PartyGroup Party { get; set; }
    public IRemoteSource? Udp { get; private set; }
    public IRemoteSource? Tcp { get; private set; }
    public long RoundTripTime { get; private set; }
    public bool InGame => Character != null;
    public ConnectionStatus Status { get; set; }
    public DateTime LastUpdateAt { get; set; } = DateTime.UtcNow;
    
    
    private long _lastTicks;
    
    private long _sequenceNumber = 0;
    
    public AvalonSession(int accountId, byte[] sessionKey)
    {
        AccountId = accountId;
        SessionKey = sessionKey;
        Party = new PartyGroup();
        Status = ConnectionStatus.Connecting;
    }
    
    public async Task PingAsync()
    {
        _lastTicks = DateTime.UtcNow.Ticks;
        var sequenceNumber = Interlocked.Increment(ref _sequenceNumber);
        var packet = SPingPacket.Create(sequenceNumber, _lastTicks);
        await SendAsync(packet);
    }
    
    public void OnPong(long sequenceNumber, long ticks)
    {
        if (sequenceNumber == _sequenceNumber)
        {
            LastUpdateAt = DateTime.UtcNow;
            var now = DateTime.UtcNow.Ticks;
            var newRtt = (now - ticks) / TimeSpan.TicksPerMillisecond;
            if (RoundTripTime != newRtt)
            {
                Console.WriteLine($"Round trip time: {RoundTripTime}ms");
            }
            RoundTripTime = newRtt;
        }
    }

    public async Task SendAsync(NetworkPacket packet)
    {
        // We might have lost connection which means the SendAsync will throw,
        // in this case, we should store the packet in a queue and then send it
        // once we have reconnected (handled by ping/pong or if could not reconnect in time).
        
        try
        {
            switch (packet.Header.Protocol)
            {
                case NetworkProtocol.Tcp:
                    if (Tcp != null)
                    {
                        await Tcp.SendAsync(packet);
                    }
                    break;
                case NetworkProtocol.Udp:
                    if (Udp != null)
                    {
                        await Udp.SendAsync(packet);
                    }
                    break;
                case NetworkProtocol.Both:
                    if (Udp != null)
                    {
                        await Udp.SendAsync(packet);
                    }
                    if (Tcp != null)
                    {
                        await Tcp.SendAsync(packet);
                    }
                    break;
                default:
                case NetworkProtocol.None:
                    throw new InvalidOperationException("Cannot send a packet with no protocol specified.");
            }
        }
        catch (IOException e)
        {
            Console.WriteLine(e);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    internal void SetUdp(UdpClientPacket udp)
    {
        Udp = udp;
    }
    
    internal void SetTcp(TcpClient tcp)
    {
        Tcp = tcp;
    }

    public void Dispose()
    {
        Tcp?.Dispose();
        Udp?.Dispose();
    }
}
