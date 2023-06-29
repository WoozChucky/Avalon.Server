using System.Collections.Concurrent;
using Avalon.Network;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;

namespace Avalon.Game;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    TimedOut,
}

public class AvalonConnection : IDisposable
{
    // Since this connection reference will be shared against multiple threads, we need to make sure
    // that the properties are thread-safe. We can do this by making them read-only and setting them
    // in the constructor. But also when we need to update them, we need to make sure that we are
    // using the Interlocked class to update them or the Volatile methods. I think ??
    
    public string Id { get; private set; }
    public IRemoteSource? Udp { get; private set; }
    public IRemoteSource? Tcp { get; private set; }
    public long RoundTripTime => Udp?.RoundTripTime ?? -1;
    public ConnectionStatus Status { get; set; }
    public DateTime LastUpdateAt { get; set; } = DateTime.UtcNow;

    private readonly ConcurrentQueue<NetworkPacket> _packetQueue;
    
    private long _lastTicks;
    
    private long _sequenceNumber = 0;
    
    public AvalonConnection(string id)
    {
        Id = id;
        _packetQueue = new ConcurrentQueue<NetworkPacket>();
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
            // RoundTripTime = newRtt;
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
                    Tcp?.SendAsync(packet);
                    break;
                case NetworkProtocol.Udp:
                    Udp?.SendAsync(packet);
                    break;
                case NetworkProtocol.Both:
                    Tcp?.SendAsync(packet);
                    Udp?.SendAsync(packet);
                    break;
                default:
                case NetworkProtocol.None:
                    throw new InvalidOperationException("Cannot send a packet with no protocol specified.");
            }
        }
        catch (IOException e)
        {
            _packetQueue.Enqueue(packet);
        }
    }
    
    public async Task SendQueuedPacketsAsync()
    {
        while (_packetQueue.Count > 0)
        {
            if (_packetQueue.TryDequeue(out var packet))
            {
                await SendAsync(packet);
            }
        }
    }
    
    internal void SetUdp(UdpClientPacket udp)
    {
        Udp = udp;
        if (Tcp != null && Status == ConnectionStatus.Connecting)
            Status = ConnectionStatus.Connected;
    }
    
    internal void SetTcp(TcpClient tcp)
    {
        Tcp = tcp;
        if (Udp != null && Status == ConnectionStatus.Connecting)
            Status = ConnectionStatus.Connected;
    }

    public void Dispose()
    {
        Tcp?.Dispose();
        Udp?.Dispose();
    }
}
