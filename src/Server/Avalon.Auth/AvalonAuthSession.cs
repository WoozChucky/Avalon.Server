using Avalon.Common.Cryptography;
using Avalon.Infrastructure;
using Avalon.Network;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto;

namespace Avalon.Auth;

public class AvalonAuthSession : AvalonSession
{
    public int AccountId { get; set; }
    
    private readonly IAvalonCryptoSession _cryptography;
    private readonly ILogger<AvalonAuthSession> _logger;

    private byte[] _handshakeData;
    private bool _verified;
    
    public AvalonAuthSession(ILoggerFactory loggerFactory, IRemoteSource connection, AsymmetricCipherKeyPair serverKeyPair, byte[] clientPublicKey) 
        : base(loggerFactory, connection)
    {
        _logger = loggerFactory.CreateLogger<AvalonAuthSession>();
        _cryptography = new AvalonCryptoSession(serverKeyPair);
        _cryptography.Initialize(clientPublicKey);
        _handshakeData = Array.Empty<byte>();
        AccountId = 0;
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
