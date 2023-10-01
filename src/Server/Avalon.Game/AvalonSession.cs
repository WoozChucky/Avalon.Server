using System.Security.Cryptography;
using Avalon.Common.Cryptography;
using Avalon.Common.Threading;
using Avalon.Database.Characters;
using Avalon.Network;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Org.BouncyCastle.Crypto;

namespace Avalon.Game;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Handshake,
    Connected,
    TimedOut,
    PendingKey
}

public class PartyGroup
{
    public bool Active { get; set; }
    public bool Leader { get; set; }
    public List<int> Members { get; set; } = new();
    
    public PartyGroup()
    {
        
    }
}

public class AvalonSession : IDisposable
{
    public int AccountId { get; set; }
    public byte[] SessionKey { get; private set; }
    public Character? Character { get; set; }
    public PartyGroup Party { get; set; }
    public IRemoteSource? Udp { get; private set; }
    public IRemoteSource? Tcp { get; private set; }
    public long RoundTripTime { get; private set; }
    public bool InGame => Character != null;
    public ConnectionStatus Status { get; set; }
    public DateTime LastUpdateAt { get; set; } = DateTime.UtcNow;
    
    private byte[] _handshakeData = Array.Empty<byte>();
    
    private long _lastTicks;
    
    private long _sequenceNumber = 0;
    
    private readonly RingBuffer<NetworkPacket> _packetQueue;
    private readonly CancellationTokenSource _cts;
    private readonly IAvalonCryptoSession _cryptography;
    private bool _verified;

    public AvalonSession(AsymmetricCipherKeyPair serverKeyPair, byte[] clientPublicKey)
    {
        AccountId = 0;
        Party = new PartyGroup();
        Status = ConnectionStatus.Connecting;
        _packetQueue = new RingBuffer<NetworkPacket>(1024);
        _cts = new CancellationTokenSource();
        _cryptography = new AvalonCryptoSession(serverKeyPair);
        _cryptography.Initialize(clientPublicKey);
        Task.Run(ProcessPacketsAsync);
    }
    
    public void InitializeCryptography(byte[] clientPublicKey)
    {
        _cryptography.Initialize(clientPublicKey);
    }
    
    public async Task PingAsync()
    {
        _lastTicks = DateTime.UtcNow.Ticks;
        var sequenceNumber = Interlocked.Increment(ref _sequenceNumber);
        var packet = SPingPacket.Create(sequenceNumber, _lastTicks);
        try
        {
            await SendAsync(packet);
        }
        catch (Exception e)
        {
            Status = ConnectionStatus.TimedOut;
        }
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
        _packetQueue.Enqueue(packet);
    }
    
    private async Task ProcessPacketsAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var packet = await _packetQueue.DequeueAsync(_cts.Token);

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
        // We might have lost connection which means the SendAsync will throw
        
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

    public byte[] Decrypt(byte[] arg)
    {
        return _cryptography.Decrypt(arg);
    }
    
    public byte[] Encrypt(byte[] arg)
    {
        return _cryptography.Encrypt(arg);
    }

    public byte[] GenerateHandshakeData()
    {
        _handshakeData = _cryptography.GenerateHandshakeData();
        return _handshakeData;
    }

    public bool VerifyHandshakeData(byte[] handshakeData)
    {
        _verified = _handshakeData.SequenceEqual(handshakeData);
        return _verified;
    }
    
    public byte[] OtherEndPublicKey()
    {
        return _cryptography.GetOtherEndPublicKey();
    }
}
