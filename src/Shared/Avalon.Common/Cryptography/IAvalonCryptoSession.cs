using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Avalon.Common.Cryptography;

/// <summary>
/// Which end of the connection a session is. It decides which derived key the session seals with
/// and which it opens with, and the order the two public keys are concatenated into the KDF salt.
/// </summary>
/// <remarks>
/// Explicit at construction rather than inferred. The two ends derive the same pair of keys and
/// use them in opposite directions, so a session that guessed wrong would complete its handshake
/// and fail to open the first packet it was sent.
/// </remarks>
public enum CryptoRole
{
    Client,
    Server,
}

public interface IAvalonCryptoSession
{
    void Initialize(byte[] otherEndPublicKeyBytes);
    byte[] GetPublicKey();
    byte[] GetOtherEndPublicKey();
    byte[] Encrypt(ReadOnlySpan<byte> data);
    int Decrypt(ReadOnlySpan<byte> data, byte[] output);
    byte[] GenerateHandshakeData();
}

/// <summary>
/// The session layer: a P-256 ECDH exchange, two AES-256-GCM keys derived from it — one per
/// direction — and a counter nonce per direction.
/// </summary>
/// <remarks>
/// <para>
/// The ECDH x-coordinate is not a key. It is fixed-width input to HKDF-SHA256, salted with both
/// public keys and labelled per direction:
/// </para>
/// <code>
/// salt = clientPublicKeyDer || serverPublicKeyDer      (that order, whichever end derives)
/// c2s  = HKDF-SHA256(secret, salt, "avalon/v1 c2s", 32)
/// s2c  = HKDF-SHA256(secret, salt, "avalon/v1 s2c", 32)
/// </code>
/// <para>
/// The salt binds the keys to this exchange: the same two peers reconnecting derive different
/// keys, and a relayed public key derives keys the relay's own exchange cannot match.
/// </para>
/// <para>
/// Nonces are a 96-bit big-endian counter from zero, one per direction, and are transmitted
/// anyway — the wire stays <c>[12-byte nonce][ciphertext][16-byte tag]</c>, and decryption uses
/// the nonce that arrived. Separate keys per direction are what make the two counters unable to
/// collide.
/// </para>
/// <para>
/// The vectors in <c>schema/crypto/session-v1.txt</c> pin all of this, so a non-.NET client can
/// check its derivation without running a server.
/// </para>
/// <para>
/// The AEAD is the platform's <see cref="AesGcm"/>, keyed once per direction in
/// <see cref="Initialize"/> and reused for every packet; the key agreement and the derivation stay
/// on BouncyCastle. Hosts check <c>AesGcm.IsSupported</c> before they serve (<c>ServerBase</c>),
/// since this assembly's netstandard2.1 target cannot.
/// </para>
/// <para>
/// The two <see cref="AesGcm"/> instances live as long as the session, which lives as long as its
/// connection, and are not disposed: a tick-thread send may still seal for a connection that has
/// just closed, and a disposed cipher would turn that into an exception on the tick. Their native
/// key handles are released, and the key material destroyed, by the handles' finalizers when the
/// session is collected.
/// </para>
/// </remarks>
public class AvalonCryptoSession : IAvalonCryptoSession
{
    // AesGcm instances are not thread-safe, and a connection seals from the tick thread while its
    // read loop opens; one lock serialises both directions, as it did with one shared cipher.
    private readonly object _lock = new object();

    private volatile bool _initialized;

    private readonly CryptoRole _role;
    private readonly AsymmetricCipherKeyPair _ownKeyPair;
    private ECPublicKeyParameters _otherEndPublicKey;
    private ECPublicKeyParameters _ownPublicKey;
    private byte[] _ownPublicKeyBytes;
    private byte[] _otherEndPublicKeyBytes;
    private readonly SecureRandom _secureRandom;
    private AesGcm _sealer;
    private AesGcm _opener;
    private readonly byte[] _sendNonce = new byte[SessionKeys.NonceSize];

    public AvalonCryptoSession(CryptoRole role, AsymmetricCipherKeyPair? keyPair = null)
    {
        _initialized = false;
        _role = role;
        _secureRandom = new SecureRandom();
        _ownKeyPair = keyPair ?? AsymmetricCipher.GenerateECDHKeyPair(256);
    }

    public void Initialize(byte[] otherEndPublicKeyBytes)
    {
        if (_initialized) throw new InvalidOperationException("Crypto session already initialized");
        _initialized = true;

        if (otherEndPublicKeyBytes == null || otherEndPublicKeyBytes.Length == 0)
            throw new ArgumentException("Invalid public key", nameof(otherEndPublicKeyBytes));

        // Parsed for the agreement, and kept verbatim for the salt. The salt is over the bytes the
        // two ends exchanged, so re-encoding here would make the derivation depend on this
        // library and the peer's agreeing on one canonical DER rather than on the transcript.
        _otherEndPublicKey = AsymmetricCipher.GetPublicKeyFromBytes(otherEndPublicKeyBytes);
        _otherEndPublicKeyBytes = (byte[])otherEndPublicKeyBytes.Clone();

        _ownPublicKey = AsymmetricCipher.GetPublicKeyFromKeyPair(_ownKeyPair);
        _ownPublicKeyBytes = AsymmetricCipher.GetPublicKeyBytes(_ownPublicKey);

        byte[] sharedSecret = AsymmetricCipher.CalculateSharedSecret(_ownKeyPair, _otherEndPublicKey);

        // Which array is the client's is the one thing the role decides here; the salt order
        // itself is fixed, so both ends build the same bytes.
        (byte[] clientToServer, byte[] serverToClient) = _role == CryptoRole.Client
            ? SessionKeys.Derive(sharedSecret, _ownPublicKeyBytes, _otherEndPublicKeyBytes)
            : SessionKeys.Derive(sharedSecret, _otherEndPublicKeyBytes, _ownPublicKeyBytes);

        // Keyed once, here, rather than per packet: re-keying GCM is most of a packet's cost. The
        // tag length is not fixed by the instance on this target; every call passes a
        // SessionKeys.TagSize span, which is what fixes it at 16 bytes.
        _sealer = new AesGcm(_role == CryptoRole.Client ? clientToServer : serverToClient);
        _opener = new AesGcm(_role == CryptoRole.Client ? serverToClient : clientToServer);

        // AesGcm imports the key, so these three are spent. Best effort only: a moving GC may
        // already have left copies elsewhere.
        Array.Clear(sharedSecret, 0, sharedSecret.Length);
        Array.Clear(clientToServer, 0, clientToServer.Length);
        Array.Clear(serverToClient, 0, serverToClient.Length);
    }

    public byte[] GetPublicKey()
    {
        return _ownPublicKeyBytes;
    }

    public byte[] GetOtherEndPublicKey()
    {
        return _otherEndPublicKeyBytes;
    }

    public byte[] Encrypt(ReadOnlySpan<byte> data)
    {
        if (!_initialized) throw new InvalidOperationException("Crypto session not initialized");

        // The one allocation: the packet itself, [nonce][ciphertext][tag], sealed in place.
        byte[] sealedPacket = new byte[SessionKeys.NonceSize + data.Length + SessionKeys.TagSize];
        Span<byte> nonce = sealedPacket.AsSpan(0, SessionKeys.NonceSize);

        lock (_lock)
        {
            // The counter is the nonce. It is sent anyway, so a peer never has to track ours. It
            // is copied out and advanced before sealing, so no two packets can share it.
            _sendNonce.CopyTo(nonce);
            SessionKeys.IncrementNonce(_sendNonce);

            _sealer.Encrypt(
                nonce,
                data,
                sealedPacket.AsSpan(SessionKeys.NonceSize, data.Length),
                sealedPacket.AsSpan(SessionKeys.NonceSize + data.Length, SessionKeys.TagSize));
        }

        return sealedPacket;
    }

    /// <exception cref="CryptographicException">
    /// The packet is shorter than a nonce and a tag, or it does not authenticate under this
    /// session's receiving key (on .NET 8 and later the platform throws the derived
    /// <c>AuthenticationTagMismatchException</c>, and clears what it had written to
    /// <paramref name="output"/>).
    /// </exception>
    public int Decrypt(ReadOnlySpan<byte> data, byte[] output)
    {
        if (!_initialized) throw new InvalidOperationException("Crypto session not initialized");
        if (data.Length < SessionKeys.NonceSize + SessionKeys.TagSize)
        {
            throw new CryptographicException("Sealed packet is shorter than its nonce and tag");
        }

        int length = data.Length - SessionKeys.NonceSize - SessionKeys.TagSize;

        lock (_lock)
        {
            _opener.Decrypt(
                data.Slice(0, SessionKeys.NonceSize),
                data.Slice(SessionKeys.NonceSize, length),
                data.Slice(SessionKeys.NonceSize + length, SessionKeys.TagSize),
                output.AsSpan(0, length));
        }

        return length;
    }

    public byte[] GenerateHandshakeData()
    {
        byte[] data = new byte[32];
        _secureRandom.NextBytes(data);
        return data;
    }
}
