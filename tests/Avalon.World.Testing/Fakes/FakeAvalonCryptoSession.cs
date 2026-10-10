using Avalon.Common.Cryptography;

namespace Avalon.World.Testing.Fakes;

/// <summary>
/// Test double for IAvalonCryptoSession. Encrypt and Decrypt are pass-throughs: no cipher and no nonce or tag, so a
/// sent payload is the serialized packet. SealInto is not supported: a fake is not a sealing session, and a test that
/// reaches it by accident fails loudly rather than framing bytes no real session writes.
/// NSubstitute cannot proxy ReadOnlySpan&lt;byte&gt; parameters; use this concrete fake instead.
/// </summary>
public sealed class FakeAvalonCryptoSession : IAvalonCryptoSession
{
    public FakeAvalonCryptoSession() => Encryptor = Encrypt;

    public void Initialize(byte[] otherEndPublicKeyBytes) { }
    public byte[] GetPublicKey() => Array.Empty<byte>();
    public byte[] GetOtherEndPublicKey() => Array.Empty<byte>();
    public byte[] Encrypt(ReadOnlySpan<byte> data) => data.ToArray();
    public EncryptFunc Encryptor { get; }

    public int SealInto(ReadOnlySpan<byte> plaintext, Span<byte> destination) =>
        throw new NotSupportedException("not a sealing session");

    public int Decrypt(ReadOnlySpan<byte> data, byte[] output)
    {
        data.CopyTo(output);
        return data.Length;
    }
    public byte[] GenerateHandshakeData() => Array.Empty<byte>();
}
