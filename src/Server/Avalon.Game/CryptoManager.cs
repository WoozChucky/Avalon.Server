using System.Security.Cryptography;
using Avalon.Common.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;

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

    public CryptoManager()
    {
        _keyPair = AsymmetricCipher.GenerateECDHKeyPair();
        _publicKey = AsymmetricCipher.GetPublicKeyFromKeyPair(_keyPair);
        _publicKeyBytes = AsymmetricCipher.GetPublicKeyBytes(_publicKey);
    }

    public byte[] GetPublicKey()
    {
        return _publicKeyBytes;
    }

    public byte[] Decrypt(byte[] key, byte[] data)
    {
        using var aesAlg = Aes.Create();
        aesAlg.Key = key;

        // Split the IV from the encrypted data
        byte[] iv = data.Take(16).ToArray();
        byte[] encryptedBytes = data.Skip(16).ToArray();

        aesAlg.IV = iv;

        // Create a decryptor to perform the stream transform
        var decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

        using var msDecrypt = new MemoryStream(encryptedBytes);
        using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
        
        byte[] decryptedBytes = new byte[encryptedBytes.Length];
        int bytesRead = csDecrypt.Read(decryptedBytes, 0, decryptedBytes.Length);

        // Resize the byte array to the actual length of the decrypted data
        Array.Resize(ref decryptedBytes, bytesRead);

        return decryptedBytes;
    }

    public byte[] Encrypt(byte[] key, byte[] data)
    {
        using var aesAlg = Aes.Create();
        aesAlg.Key = key;
        aesAlg.GenerateIV();

        // Create an encryptor to perform the stream transform
        ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

        using var msEncrypt = new MemoryStream();
        using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
        using var swEncrypt = new StreamWriter(csEncrypt);
        // Write all data to the stream
        swEncrypt.Write(data);

        // Combine the IV and encrypted data
        var encrypted = aesAlg.IV.Concat(msEncrypt.ToArray()).ToArray();
        return encrypted;
    }
}