using System.Security.Cryptography;
using Avalon.Common.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Avalon.Game;

public interface ICryptoManager
{
    byte[] GetPublicKey();
    AsymmetricCipherKeyPair GetKeyPair();
    int GetValidKeySize();
}

public class CryptoManager : ICryptoManager
{
    private readonly byte[] _publicKeyBytes;
    private readonly ECPublicKeyParameters _publicKey;
    private readonly AsymmetricCipherKeyPair _keyPair;
    private readonly SecureRandom _secureRandom;

    public CryptoManager()
    {
        _keyPair = AsymmetricCipher.GenerateECDHKeyPair(256);
        _publicKey = AsymmetricCipher.GetPublicKeyFromKeyPair(_keyPair);
        _publicKeyBytes = AsymmetricCipher.GetPublicKeyBytes(_publicKey);
        _secureRandom = new SecureRandom();
        // Make sure the RNG is properly seeded so next usages are faster
        _secureRandom.SetSeed(DateTime.UtcNow.Ticks);
        var tempBytes = new byte[32];
        _secureRandom.NextBytes(tempBytes);
    }

    public byte[] GetPublicKey()
    {
        return _publicKeyBytes;
    }

    public AsymmetricCipherKeyPair GetKeyPair()
    {
        return _keyPair;
    }

    public int GetValidKeySize()
    {
        return _publicKeyBytes.Length;
    }
}
