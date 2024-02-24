using Org.BouncyCastle.Crypto;

namespace Avalon.Common.Cryptography;

public interface ICryptoManager
{
    byte[] GetPublicKey();
    AsymmetricCipherKeyPair GetKeyPair();
    int GetValidKeySize();
}

public class CryptoManager : ICryptoManager
{
    private readonly byte[] _publicKeyBytes;
    private readonly AsymmetricCipherKeyPair _keyPair;

    public CryptoManager()
    {
        _keyPair = AsymmetricCipher.GenerateECDHKeyPair(256);
        var publicKey = AsymmetricCipher.GetPublicKeyFromKeyPair(_keyPair);
        _publicKeyBytes = AsymmetricCipher.GetPublicKeyBytes(publicKey);
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
