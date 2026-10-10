using Avalon.Common.Cryptography;

namespace Avalon.Server.Auth.UnitTests;

/// <summary>
/// Test double for IAvalonCryptoSession. Encrypt, SealInto and Decrypt are pass-throughs: no cipher and no
/// nonce or tag, so a sent payload is the serialized packet, and the three agree with each other.
/// NSubstitute cannot proxy ReadOnlySpan&lt;byte&gt; parameters; use this concrete fake instead. The world's
/// Avalon.World.Testing has the same fake, but it brings in the world server, and this one also records the
/// Initialize calls the handshake tests check.
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
        // No cipher and no framing, as Encrypt: the plaintext as-is, which Decrypt hands back unchanged.
        plaintext.CopyTo(destination);
        return plaintext.Length;
    }

    public int Decrypt(ReadOnlySpan<byte> data, byte[] output)
    {
        data.CopyTo(output);
        return data.Length;
    }
    public byte[] GenerateHandshakeData() => Array.Empty<byte>();
}
