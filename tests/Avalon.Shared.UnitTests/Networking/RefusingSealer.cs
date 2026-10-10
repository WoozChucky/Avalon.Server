using Avalon.Common.Cryptography;

namespace Avalon.Shared.UnitTests.Networking;

/// <summary>A session whose every seal fails, as a spent or uninitialised one does.</summary>
internal sealed class RefusingSealer : IAvalonCryptoSession
{
    public EncryptFunc Encryptor => throw new NotSupportedException();
    public void Initialize(byte[] otherEndPublicKeyBytes) => throw new NotSupportedException();
    public byte[] GetPublicKey() => throw new NotSupportedException();
    public byte[] GetOtherEndPublicKey() => throw new NotSupportedException();
    public byte[] Encrypt(ReadOnlySpan<byte> data) => throw new NotSupportedException();

    public int SealInto(ReadOnlySpan<byte> plaintext, Span<byte> destination) =>
        throw new InvalidOperationException("The session cannot seal");

    public int Decrypt(ReadOnlySpan<byte> data, byte[] output) => throw new NotSupportedException();
    public byte[] GenerateHandshakeData() => throw new NotSupportedException();
}
