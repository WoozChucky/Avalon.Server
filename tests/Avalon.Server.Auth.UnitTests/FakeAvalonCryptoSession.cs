using Avalon.Common.Cryptography;

namespace Avalon.Server.Auth.UnitTests;

/// <summary>
/// Test double for IAvalonCryptoSession. Encrypt is a pass-through (returns plaintext as-is).
/// NSubstitute cannot proxy ReadOnlySpan&lt;byte&gt; parameters; use this concrete fake instead.
/// </summary>
internal sealed class FakeAvalonCryptoSession : IAvalonCryptoSession
{
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
    public byte[] Decrypt(byte[] data) => data;
    public byte[] GenerateHandshakeData() => Array.Empty<byte>();
}
