using Avalon.Common.Cryptography;

namespace Avalon.Server.World.UnitTests;

/// <summary>
/// Test double for IAvalonCryptoSession. Encrypt is a pass-through (returns plaintext as-is).
/// NSubstitute cannot proxy ReadOnlySpan&lt;byte&gt; parameters; use this concrete fake instead.
/// </summary>
internal sealed class FakeAvalonCryptoSession : IAvalonCryptoSession
{
    public void Initialize(byte[] otherEndPublicKeyBytes) { }
    public byte[] GetPublicKey() => Array.Empty<byte>();
    public byte[] GetOtherEndPublicKey() => Array.Empty<byte>();
    public byte[] Encrypt(ReadOnlySpan<byte> data) => data.ToArray();
    public int Decrypt(ReadOnlySpan<byte> data, byte[] output)
    {
        data.CopyTo(output);
        return data.Length;
    }
    public byte[] GenerateHandshakeData() => Array.Empty<byte>();
}
