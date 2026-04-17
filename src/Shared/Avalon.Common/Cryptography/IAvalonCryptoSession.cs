using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Avalon.Common.Cryptography;

public interface IAvalonCryptoSession
{
    void Initialize(byte[] otherEndPublicKeyBytes);
    byte[] GetPublicKey();
    byte[] GetOtherEndPublicKey();
    byte[] Encrypt(ReadOnlySpan<byte> data);
    int Decrypt(ReadOnlySpan<byte> data, byte[] output);
    byte[] GenerateHandshakeData();
}

public class AvalonCryptoSession : IAvalonCryptoSession
{
    private readonly object _lock = new object();

    private volatile bool _initialized;

    private readonly AsymmetricCipherKeyPair _ownKeyPair;
    private ECPublicKeyParameters _otherEndPublicKey;
    private ECPublicKeyParameters _ownPublicKey;
    private IBufferedCipher _encryptCipher;
    private byte[] _ownPublicKeyBytes;
    private byte[] _otherEndPublicKeyBytes;
    private readonly SecureRandom _secureRandom;
    private byte[] _sessionKey;
    private readonly byte[] _nonce = new byte[12];

    public AvalonCryptoSession(AsymmetricCipherKeyPair? keyPair = null)
    {
        _initialized = false;
        _secureRandom = new SecureRandom();
        _secureRandom.SetSeed(DateTime.UtcNow.Ticks);
        var tempBytes = new byte[16];
        _secureRandom.NextBytes(tempBytes);
        _secureRandom.NextBytes(_nonce);
        _ownKeyPair = keyPair ?? AsymmetricCipher.GenerateECDHKeyPair(256);
    }

    public void Initialize(byte[] otherEndPublicKeyBytes)
    {
        if (_initialized) throw new InvalidOperationException("Crypto session already initialized");
        _initialized = true;

        if (otherEndPublicKeyBytes == null || otherEndPublicKeyBytes.Length == 0)
            throw new ArgumentException("Invalid public key", nameof(otherEndPublicKeyBytes));

        // Parse the byte array to reconstruct the public key
        _otherEndPublicKey = AsymmetricCipher.GetPublicKeyFromBytes(otherEndPublicKeyBytes);

        _otherEndPublicKeyBytes = AsymmetricCipher.GetPublicKeyBytes(_otherEndPublicKey);

        _sessionKey = AsymmetricCipher.CalculateSharedSecret(_ownKeyPair, _otherEndPublicKey);

        _ownPublicKey = AsymmetricCipher.GetPublicKeyFromKeyPair(_ownKeyPair);

        _ownPublicKeyBytes = AsymmetricCipher.GetPublicKeyBytes(_ownPublicKey);

        _encryptCipher = CipherUtilities.GetCipher("AES/GCM/NoPadding");
    }

    public byte[] GetPublicKey()
    {
        return _ownPublicKeyBytes;
    }

    public byte[] GetOtherEndPublicKey()
    {
        return _otherEndPublicKeyBytes;
    }

    public byte[] Encrypt(ReadOnlySpan<byte> data)
    {
        if (!_initialized) throw new InvalidOperationException("Crypto session not initialized");

        lock (_lock)
        {
            // Generate a random 96-bit nonce (IV)
            var nonce = new byte[12];
            _secureRandom.NextBytes(nonce);

            // Create an AES-GCM cipher
            var parameters = new ParametersWithIV(new KeyParameter(_sessionKey), nonce);
            _encryptCipher.Init(true, parameters);

            // Encrypt the data
            var ciphertext = _encryptCipher.DoFinal(data.ToArray());

            _encryptCipher.Reset();

            // Combine the nonce and ciphertext
            var encryptedData = nonce.Concat(ciphertext).ToArray();

            return encryptedData;
        }
    }

    public int Decrypt(ReadOnlySpan<byte> data, byte[] output)
    {
        if (!_initialized) throw new InvalidOperationException("Crypto session not initialized");

        lock (_lock)
        {
            ReadOnlySpan<byte> nonce = data[..12];
            byte[] ciphertext = data[12..].ToArray(); // BouncyCastle 2.6.2 IBufferedCipher only provides byte[] overloads on netstandard2.0 — span input requires one copy here

            var parameters = new ParametersWithIV(new KeyParameter(_sessionKey), nonce.ToArray());
            _encryptCipher.Init(false, parameters);

            int len = _encryptCipher.ProcessBytes(ciphertext, 0, ciphertext.Length, output, 0);
            len += _encryptCipher.DoFinal(output, len);
            _encryptCipher.Reset();

            return len;
        }
    }

    public byte[] GenerateHandshakeData()
    {
        var data = new byte[32];
        _secureRandom.NextBytes(data);
        return data;
    }
}
