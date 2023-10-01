namespace Avalon.Common.Cryptography;

public interface IAvalonCryptoSession
{
    byte[] Encrypt(byte[] data);
    byte[] Decrypt(byte[] data);
}
