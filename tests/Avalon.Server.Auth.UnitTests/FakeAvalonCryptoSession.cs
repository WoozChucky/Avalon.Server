using Avalon.Common.Cryptography;

namespace Avalon.Server.Auth.UnitTests;

/// <summary>
/// Test double for IAvalonCryptoSession. Encrypt is a pass-through (returns plaintext as-is).
/// NSubstitute cannot proxy ReadOnlySpan&lt;byte&gt; parameters; use this concrete fake instead.
/// </summary>
internal sealed class FakeAvalonCryptoSession : IAvalonCryptoSession
{
    public FakeAvalonCryptoSession() => Encryptor = Encrypt;

    public int InitializeCallCount { get; private set; }
    public byte[]? LastInitializedKey { get; private set; }

    public void Initialize(byte[] otherEndPublicKeyBytes)
    {
        InitializeCallCount++;
        LastInitializedKey = otherEndPublicKeyBytes;
    }

    public byte[] GetPublicKey() => Array.Empty<byte>();
    public byte[] GetOtherEndPublicKey() => Array.Empty<byte>();
    public byte[] Encrypt(ReadOnlySpan<byte> data) => data.ToArray();
    public EncryptFunc Encryptor { get; }

    public int SealInto(ReadOnlySpan<byte> plaintext, Span<byte> destination)
    {
        // No cipher: the plaintext between a zero nonce and a zero tag, at the sealed layout's length.
        destination.Slice(0, SessionKeys.NonceSize).Clear();
        plaintext.CopyTo(destination.Slice(SessionKeys.NonceSize));
        destination.Slice(SessionKeys.NonceSize + plaintext.Length, SessionKeys.TagSize).Clear();
        return SessionKeys.NonceSize + plaintext.Length + SessionKeys.TagSize;
    }

    public int Decrypt(ReadOnlySpan<byte> data, byte[] output)
    {
        data.CopyTo(output);
        return data.Length;
    }
    public byte[] GenerateHandshakeData() => Array.Empty<byte>();
}
