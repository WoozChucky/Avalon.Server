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
    private long _lastTicks;
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
                _logger.LogInformation("[{CharName}] Round trip time changed: {RoundTripTime}ms -> {NewRtt}ms", Character?.Name ?? AccountId.ToString(), RoundTripTime, newRtt);
            }
            RoundTripTime = (int) newRtt;
        }
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
