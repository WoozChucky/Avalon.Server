using System.Security.Cryptography;
using System.Text;
using Avalon.Common.GameAuth;

namespace Avalon.Infrastructure.GameAuth;

/// <summary>Domain-separated keys derived from the existing private host key. Replay responses are encrypted, not plaintext bearer credentials.</summary>
public sealed class GameAuthCryptography
{
    private const int AesNonceBytes = 12;
    private const int AesTagBytes = 16;
    private const int EnvelopeHeaderBytes = AesNonceBytes + AesTagBytes;
    private readonly byte[] _proofKey;
    private readonly byte[] _receiptKey;
    public GameAuthCryptography(byte[] privateHostKey)
    {
        if (privateHostKey.Length < GameAuthPolicy.TokenBytes) throw new ArgumentException("A private 256-bit host key is required.", nameof(privateHostKey));
        _proofKey = HMACSHA256.HashData(privateHostKey, Encoding.UTF8.GetBytes("avalon.game-auth.proof.v1"));
        _receiptKey = HMACSHA256.HashData(privateHostKey, Encoding.UTF8.GetBytes("avalon.game-auth.receipt.v1"));
    }

    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(GameAuthPolicy.TokenBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static bool IsToken(string? value) => value is { Length: GameAuthPolicy.TokenCharacters } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    public static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public string ProofDigest(string hex) => Convert.ToHexStringLower(HMACSHA256.HashData(_proofKey, Convert.FromHexString(hex)));
    public string ProviderProofDigest(string provider, string proof) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_proofKey, Encoding.UTF8.GetBytes(provider + "\0" + proof)));
    public string Binding(string operation, Guid requestId, string digest) => Digest($"{operation}:{requestId:N}:{digest}");

    public string Protect(GameAuthReply reply, string binding) => ProtectText(GameAuthJson.Serialize(reply), binding);

    public string ProtectText(string value, string binding)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        byte[] nonce = RandomNumberGenerator.GetBytes(AesNonceBytes);
        byte[] tag = new byte[AesTagBytes];
        byte[] cipher = new byte[bytes.Length];
        using var aes = new AesGcm(_receiptKey, AesTagBytes);
        aes.Encrypt(nonce, bytes, cipher, tag, Encoding.UTF8.GetBytes(binding));
        CryptographicOperations.ZeroMemory(bytes);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public GameAuthReply Unprotect(string envelope, string binding) =>
        GameAuthJson.Deserialize<GameAuthReply>(UnprotectText(envelope, binding)) ?? throw new CryptographicException("Invalid game receipt.");

    public string UnprotectText(string envelope, string binding)
    {
        byte[] bytes = Convert.FromBase64String(envelope);
        if (bytes.Length < EnvelopeHeaderBytes || bytes.Length > GameAuthPolicy.MaximumBodyBytes) throw new CryptographicException("Invalid game receipt.");
        byte[] clear = new byte[bytes.Length - EnvelopeHeaderBytes];
        using var aes = new AesGcm(_receiptKey, AesTagBytes);
        aes.Decrypt(bytes.AsSpan(0, AesNonceBytes), bytes.AsSpan(EnvelopeHeaderBytes), bytes.AsSpan(AesNonceBytes, AesTagBytes), clear, Encoding.UTF8.GetBytes(binding));
        try { return Encoding.UTF8.GetString(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
}
