using System;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace Avalon.Common.Cryptography;

/// <summary>
/// The v1 session key derivation: HKDF-SHA256 over the ECDH shared secret, salted with both
/// public keys and labelled per direction.
/// </summary>
/// <remarks>
/// <para>
/// Separated from <see cref="AvalonCryptoSession"/> so that the exported known-answer vectors in
/// <c>schema/crypto/session-v1.txt</c> come from the code a live connection runs, rather than
/// from a second copy of it that can drift.
/// </para>
/// <code>
/// ikm  = the 32-byte ECDH shared secret, leading zeros kept
/// salt = clientPublicKeyDer || serverPublicKeyDer
/// c2s  = HKDF-SHA256(ikm, salt, "avalon/v1 c2s", 32)
/// s2c  = HKDF-SHA256(ikm, salt, "avalon/v1 s2c", 32)
/// </code>
/// </remarks>
public static class SessionKeys
{
    /// <summary>AES-256.</summary>
    public const int KeySize = 32;

    /// <summary>The GCM standard nonce length, and the width of the per-direction counter.</summary>
    public const int NonceSize = 12;

    /// <summary>The version this derivation is. It appears in both labels and in the vector file.</summary>
    public const string Version = "avalon/v1";

    private const string ClientToServerLabel = Version + " c2s";
    private const string ServerToClientLabel = Version + " s2c";

    /// <summary>The HKDF <c>info</c> for the client-to-server key. ASCII, no terminator.</summary>
    public static byte[] ClientToServerInfo() => Ascii(ClientToServerLabel);

    /// <summary>The HKDF <c>info</c> for the server-to-client key. ASCII, no terminator.</summary>
    public static byte[] ServerToClientInfo() => Ascii(ServerToClientLabel);

    /// <summary>
    /// The HKDF salt: the two public keys as they were exchanged, client's first, whichever end
    /// is deriving.
    /// </summary>
    /// <remarks>
    /// Fixing the order is what lets both ends reach the same salt without either reordering by
    /// role. Including the keys at all is what binds the derivation to this exchange — the same
    /// two peers reconnecting derive different keys, and a relay that substitutes a public key
    /// derives keys its own exchange cannot match.
    /// </remarks>
    public static byte[] Salt(byte[] clientPublicKeyDer, byte[] serverPublicKeyDer)
    {
        if (clientPublicKeyDer == null) throw new ArgumentNullException(nameof(clientPublicKeyDer));
        if (serverPublicKeyDer == null) throw new ArgumentNullException(nameof(serverPublicKeyDer));

        var salt = new byte[clientPublicKeyDer.Length + serverPublicKeyDer.Length];
        Buffer.BlockCopy(clientPublicKeyDer, 0, salt, 0, clientPublicKeyDer.Length);
        Buffer.BlockCopy(serverPublicKeyDer, 0, salt, clientPublicKeyDer.Length, serverPublicKeyDer.Length);
        return salt;
    }

    /// <summary>The two directional keys for one exchange.</summary>
    public static (byte[] ClientToServer, byte[] ServerToClient) Derive(
        byte[] sharedSecret,
        byte[] clientPublicKeyDer,
        byte[] serverPublicKeyDer)
    {
        if (sharedSecret == null) throw new ArgumentNullException(nameof(sharedSecret));

        byte[] salt = Salt(clientPublicKeyDer, serverPublicKeyDer);

        return (
            Expand(sharedSecret, salt, ClientToServerInfo()),
            Expand(sharedSecret, salt, ServerToClientInfo()));
    }

    /// <summary>
    /// The nonce a direction's <paramref name="counter"/>'th sealed packet carries: a 96-bit
    /// big-endian count from zero.
    /// </summary>
    public static byte[] Nonce(ulong counter)
    {
        var nonce = new byte[NonceSize];

        for (int i = NonceSize - 1; i >= 0 && counter != 0; i--)
        {
            nonce[i] = (byte)counter;
            counter >>= 8;
        }

        return nonce;
    }

    /// <summary>96-bit big-endian increment, in place.</summary>
    /// <exception cref="OverflowException">
    /// Every byte wrapped, so the next nonce would repeat one already used under this key. 2^96
    /// packets on one session is not reachable; failing is still cheaper than the forgery a
    /// reused GCM nonce permits.
    /// </exception>
    public static void IncrementNonce(byte[] nonce)
    {
        if (nonce == null) throw new ArgumentNullException(nameof(nonce));

        for (int i = nonce.Length - 1; i >= 0; i--)
        {
            if (++nonce[i] != 0) return;
        }

        throw new OverflowException("Session nonce counter exhausted");
    }

    private static byte[] Expand(byte[] sharedSecret, byte[] salt, byte[] info)
    {
        // HKDF-SHA256 through BouncyCastle rather than System.Security.Cryptography.HKDF, which
        // this assembly's netstandard2.1 target does not carry. SessionKeyDerivationShould holds
        // the two implementations against each other, and against RFC 5869.
        var hkdf = new HkdfBytesGenerator(new Sha256Digest());
        hkdf.Init(new HkdfParameters(sharedSecret, salt, info));

        var key = new byte[KeySize];
        hkdf.GenerateBytes(key, 0, KeySize);
        return key;
    }

    private static byte[] Ascii(string value)
    {
        var bytes = new byte[value.Length];
        for (int i = 0; i < value.Length; i++) bytes[i] = (byte)value[i];
        return bytes;
    }
}
