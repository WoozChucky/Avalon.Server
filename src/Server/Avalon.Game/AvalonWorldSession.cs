using Avalon.Common.Cryptography;
using Avalon.Domain.Characters;
using Avalon.Infrastructure;
using Avalon.Network;
using Avalon.Network.Packets.Generic;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto;

namespace Avalon.Game;

public class PartyGroup
{
    public bool Active { get; set; }
    public bool Leader { get; set; }
    public List<int> Members { get; set; } = new();
    
    public PartyGroup()
    {
        
    }
}

public class AvalonWorldSession : AvalonSession
{
    public int AccountId { get; set; }
    public Character? Character { get; set; }
    public PartyGroup Party { get; set; }
    
    public bool InGame => Character != null;
    public bool InMap => InGame && Character!.InstanceId != null;
    
    private readonly IAvalonCryptoSession _cryptography;
    private readonly ILogger<AvalonWorldSession> _logger;
    
    private byte[] _handshakeData = Array.Empty<byte>();
    private long _lastClientTicks = 0;
    private long _lastServerTicks = 0;
    private long _timeSyncOffset = 0;
    private long _sequenceNumber = 0;
    private bool _verified;
    
    public AvalonWorldSession(ILoggerFactory loggerFactory, IRemoteSource source, AsymmetricCipherKeyPair serverKeyPair, byte[] clientPublicKey) 
        : base(loggerFactory, source)
    {
        
        _logger = loggerFactory.CreateLogger<AvalonWorldSession>();
        _cryptography = new AvalonCryptoSession(serverKeyPair);
        _cryptography.Initialize(clientPublicKey);
        AccountId = 0;
        Party = new PartyGroup();
    }
    
    public async Task PingAsync()
    {
        Interlocked.Increment(ref _sequenceNumber);
        
        _lastServerTicks = DateTime.UtcNow.Ticks;
        var packet = SPingPacket.Create(_lastServerTicks, _lastClientTicks, RoundTripTime, _timeSyncOffset);
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
    
    public void OnPong(long lastServerTimestamp, long clientReceivedTimestamp, long clientSentTimestamp)
    {
        var rtt = ((clientReceivedTimestamp - lastServerTimestamp) + (DateTime.UtcNow.Ticks - clientSentTimestamp));
        var latency = rtt / TimeSpan.TicksPerMillisecond;
        
        _timeSyncOffset = _lastServerTicks + (rtt / 2) - _lastClientTicks;
        
        if (Latency - latency >  20)
        {
            _logger.LogInformation("[{CharName}] Latency changed: {Latency}ms -> {NewLatency}ms", Character?.Name ?? AccountId.ToString(), Latency, latency);
        }
        
        _lastClientTicks = clientReceivedTimestamp;
        RoundTripTime = (int) rtt;
        Latency = (int) latency;
        _logger.LogDebug("[{CharName}] RTT: {Rtt}ticks, Latency: {Latency}ms", Character?.Name ?? AccountId.ToString(), RoundTripTime, Latency);
        LastUpdateAt = DateTime.UtcNow;
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
