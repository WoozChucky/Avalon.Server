using Avalon.Common.Cryptography;
using Avalon.Common.Threading;
using Avalon.Database.Characters;
using Avalon.Domain.Characters;
using Avalon.Network;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto;

namespace Avalon.Game;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Handshake,
    Connected,
    TimedOut
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
    public Character? Character { get; set; }
    public PartyGroup Party { get; set; }
    public IRemoteSource? Connection { get; private set; }
    public int RoundTripTime { get; private set; }
    public bool InGame => Character != null;
    public bool InMap => InGame && Character!.InstanceId != null;
    public ConnectionStatus Status { get; set; }
    public DateTime LastUpdateAt { get; set; } = DateTime.UtcNow;
    
    private byte[] _handshakeData = Array.Empty<byte>();
    
    private long _lastTicks;
    
    private long _sequenceNumber = 0;
    
    private readonly RingBuffer<NetworkPacket> _packetQueue;
    private readonly ILogger<AvalonSession> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly IAvalonCryptoSession _cryptography;
    private bool _verified;

    public AvalonSession(ILoggerFactory loggerFactory, AsymmetricCipherKeyPair serverKeyPair, byte[] clientPublicKey)
    {
        _logger = loggerFactory.CreateLogger<AvalonSession>();
        AccountId = 0;
        Party = new PartyGroup();
        Status = ConnectionStatus.Connecting;
        _packetQueue = new RingBuffer<NetworkPacket>("SND",1024);
        _cts = new CancellationTokenSource();
        _cryptography = new AvalonCryptoSession(serverKeyPair);
        _cryptography.Initialize(clientPublicKey);
        Task.Run(ProcessPacketsAsync);
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
            _logger.LogWarning(e, "Failed to send ping packet");
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
            if (RoundTripTime - newRtt >  20)
            {
                _logger.LogInformation("Round trip time changed: {RoundTripTime}ms -> {NewRtt}ms", RoundTripTime, newRtt);
            }
            RoundTripTime = (int) newRtt;
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
                try
                {
                    var packet = await _packetQueue.DequeueAsync(_cts.Token);

                    if (packet is null)
                    {
                        _logger.LogWarning("Packet was null for session {SessionId}", AccountId);
                        continue;
                    }
                
                    await SendQueuedPacketAsync(packet);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Lost connection to session {SessionId}", AccountId);
                    break;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to process packet");
                }
            }
        }
        catch (OperationCanceledException)
        {
            
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to process packets");
        }
    }
    
    private async Task SendQueuedPacketAsync(NetworkPacket packet)
    {
        switch (packet.Header.Protocol)
        {
            case NetworkProtocol.Tcp:
                if (Connection != null)
                {
                    await Connection.SendAsync(packet);
                }
                else
                {
                    _logger.LogWarning("Connection was null for session {SessionId}", AccountId);
                }
                break;
            case NetworkProtocol.Udp:
                throw new NotSupportedException("Cannot send UDP packets to a remote source.");
            case NetworkProtocol.Both:
                throw new NotSupportedException("Cannot send Both packet types to a remote source.");
            default:
            case NetworkProtocol.None:
                throw new InvalidOperationException("Cannot send a packet with no protocol specified.");
        }
    }
    
    internal void SetConnection(IRemoteSource source)
    {
        Connection = source;
    }

    public void Dispose()
    {
        Connection?.Dispose();
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
