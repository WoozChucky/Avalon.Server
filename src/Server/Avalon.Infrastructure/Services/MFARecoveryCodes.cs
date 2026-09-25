using System.Security.Cryptography;
using System.Text;

namespace Avalon.Infrastructure.Services;

/// <summary>
/// Generates, hashes and verifies MFA recovery codes.
/// </summary>
/// <remarks>
/// <para>
/// A code is 80 bits from the CSPRNG (<see cref="ISecureRandom"/>), written as 16 Crockford
/// base32 characters in four groups of four, e.g. <c>VTPV-XVR1-4D2P-F2DB</c>. Crockford's alphabet
/// leaves out I, L, O and U, so nothing on screen can be misread as another character. 80 bits
/// divides into 5-bit characters exactly, so every character is uniform with no modulo bias.
/// A code must never be derived from a <see cref="Guid"/> of any version.
/// </para>
/// <para>
/// Only the SHA-256 of the canonical code (upper case, no separators) is stored. A slow password
/// hash buys nothing here: it exists to slow brute force of low-entropy human passwords, whereas
/// guessing one 80-bit random code offline means ~2^80 SHA-256 evaluations, which is out of reach.
/// A salt is also unnecessary, since every code is unique random data and a precomputed table
/// over 2^80 inputs cannot exist. This matches how refresh tokens are stored.
/// </para>
/// <para>
/// A stored value that is not a 32-byte hash, such as a pre-#464 plaintext code or an empty value
/// written while setup is unconfirmed, never verifies.
/// </para>
/// </remarks>
public static class MFARecoveryCodes
{
    public const int Count = 3;
    public const int EntropyBytes = 10;
    public const int CodeLength = 16;
    private const int GroupLength = 4;
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Generates one code for display. Show it once; store only <see cref="Hash"/>.</summary>
    public static string Generate(ISecureRandom random)
    {
        var bytes = random.GetBytes(EntropyBytes);
        if (bytes.Length != EntropyBytes)
            throw new InvalidOperationException($"Expected {EntropyBytes} random bytes, got {bytes.Length}.");

        try
        {
            var chars = new char[CodeLength];
            var buffer = 0;
            var bits = 0;
            var index = 0;
            foreach (var b in bytes)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    bits -= 5;
                    chars[index++] = Alphabet[(buffer >> bits) & 0x1F];
                }
                buffer &= (1 << bits) - 1;
            }

            var sb = new StringBuilder(CodeLength + CodeLength / GroupLength - 1);
            for (var i = 0; i < CodeLength; i++)
            {
                if (i > 0 && i % GroupLength == 0)
                    sb.Append('-');
                sb.Append(chars[i]);
            }
            return sb.ToString();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>SHA-256 of the canonical form of a code. Returns null if the input is not a well-formed code.</summary>
    public static byte[]? Hash(string? code)
    {
        var canonical = Canonicalize(code);
        return canonical == null ? null : SHA256.HashData(Encoding.ASCII.GetBytes(canonical));
    }

    /// <summary>
    /// Constant-time check of a user-supplied code against a stored hash.
    /// </summary>
    public static bool Matches(string? code, byte[]? storedHash)
    {
        if (storedHash is not { Length: SHA256.HashSizeInBytes })
            return false;

        var candidate = Hash(code);
        return candidate != null && CryptographicOperations.FixedTimeEquals(candidate, storedHash);
    }

    private static string? Canonicalize(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return null;

        var sb = new StringBuilder(CodeLength);
        foreach (var c in code)
        {
            if (c == '-' || char.IsWhiteSpace(c))
                continue;

            var upper = char.ToUpperInvariant(c);
            if (Alphabet.IndexOf(upper) < 0 || sb.Length == CodeLength)
                return null;
            sb.Append(upper);
        }

        return sb.Length == CodeLength ? sb.ToString() : null;
    }
}
