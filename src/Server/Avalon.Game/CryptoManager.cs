using System.Security.Cryptography;
using Avalon.Common.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Avalon.Game;

public interface ICryptoManager
{
    byte[] GetPublicKey();
    byte[] Decrypt(byte[] key, byte[] data);
    byte[] Encrypt(byte[] key, byte[] data);
}

public class CryptoManager : ICryptoManager
{
    private readonly byte[] _publicKeyBytes;
    private readonly ECPublicKeyParameters _publicKey;
    private readonly AsymmetricCipherKeyPair _keyPair;
    private readonly SecureRandom _secureRandom;

    public CryptoManager()
    {
        _keyPair = AsymmetricCipher.GenerateECDHKeyPair();
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

    public byte[] Encrypt(byte[] key, byte[] data)
    {
        // Generate a random 96-bit nonce (IV)
        var nonce = new byte[12];
        _secureRandom.NextBytes(nonce);

        // Create an AES-GCM cipher
        var cipher = CipherUtilities.GetCipher("AES/GCM/NoPadding");
        var parameters = new ParametersWithIV(new KeyParameter(key), nonce);
        cipher.Init(true, parameters);

        // Encrypt the data
        var ciphertext = cipher.DoFinal(data);

        // Combine the nonce and ciphertext
        var encryptedData = nonce.Concat(ciphertext).ToArray();
        
        return encryptedData;
    }
    
    public byte[] Decrypt(byte[] key, byte[] data)
    {
        // Split the nonce (IV) and ciphertext
        var nonce = data.Take(12).ToArray();
        var ciphertext = data.Skip(12).ToArray();

        // Create an AES-GCM cipher with BouncyCastle
        var cipher = CipherUtilities.GetCipher("AES/GCM/NoPadding");
        var parameters = new ParametersWithIV(new KeyParameter(key), nonce);
        cipher.Init(false, parameters);

        // Decrypt the data
        var decryptedData = cipher.DoFinal(ciphertext);

        return decryptedData;
    }
}
