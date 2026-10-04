using System.Security.Cryptography;
using System.Text;

namespace Avalon.Infrastructure.GameAuth;

/// <summary>Domain-separated keys derived from the existing private host key. Replay responses are encrypted, not plaintext bearer credentials.</summary>
public sealed class GameAuthCryptography
{
    private readonly byte[] _proofKey;
    private readonly byte[] _receiptKey;
    public GameAuthCryptography(byte[] privateHostKey)
    {
        if (privateHostKey.Length < 32) throw new ArgumentException("A private 256-bit host key is required.", nameof(privateHostKey));
        _proofKey = HMACSHA256.HashData(privateHostKey, Encoding.UTF8.GetBytes("avalon.game-auth.proof.v1"));
        _receiptKey = HMACSHA256.HashData(privateHostKey, Encoding.UTF8.GetBytes("avalon.game-auth.receipt.v1"));
    }

    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static bool IsToken(string? value) => value is { Length: 43 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    public static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public string ProofDigest(string hex) => Convert.ToHexStringLower(HMACSHA256.HashData(_proofKey, Convert.FromHexString(hex)));
    public string Binding(string operation, Guid requestId, string digest) => Digest($"{operation}:{requestId:N}:{digest}");

    public string Protect(GameAuthReply reply, string binding) => ProtectText(GameAuthJson.Serialize(reply), binding);

    public string ProtectText(string value, string binding)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[bytes.Length];
        using var aes = new AesGcm(_receiptKey, 16);
        aes.Encrypt(nonce, bytes, cipher, tag, Encoding.UTF8.GetBytes(binding));
        CryptographicOperations.ZeroMemory(bytes);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public GameAuthReply Unprotect(string envelope, string binding) =>
        GameAuthJson.Deserialize<GameAuthReply>(UnprotectText(envelope, binding)) ?? throw new CryptographicException("Invalid game receipt.");

    public string UnprotectText(string envelope, string binding)
    {
        var bytes = Convert.FromBase64String(envelope);
        if (bytes.Length < 28 || bytes.Length > 16384) throw new CryptographicException("Invalid game receipt.");
        var clear = new byte[bytes.Length - 28];
        using var aes = new AesGcm(_receiptKey, 16);
        aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), clear, Encoding.UTF8.GetBytes(binding));
        try { return Encoding.UTF8.GetString(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
}
