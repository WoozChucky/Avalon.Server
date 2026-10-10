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

/// <remarks>
/// One thread at a time seals, and one thread at a time opens; concurrent seals, or concurrent opens, are the caller's
/// to prevent (#875). A seal and an open may run at once: each direction has its own cipher and its own state.
/// </remarks>
public interface IAvalonCryptoSession
{
    void Initialize(byte[] otherEndPublicKeyBytes);
    byte[] GetPublicKey();
    byte[] GetOtherEndPublicKey();
    byte[] Encrypt(ReadOnlySpan<byte> data);

    /// <summary>
    /// <see cref="Encrypt" /> as an <see cref="EncryptFunc" />, created once with the session. A client-to-server packet's
    /// <c>Create</c> takes this rather than the method group <c>Encrypt</c>: a method group is a new delegate each time it
    /// is converted, and whether the JIT keeps that on the stack depends on how far it has optimised the caller (#854).
    /// </summary>
    EncryptFunc Encryptor { get; }

    /// <summary>
    /// Seals <paramref name="plaintext" /> into <paramref name="destination" /> as
    /// <c>[12-byte nonce][ciphertext][16-byte tag]</c> and returns that length, the plaintext's plus 28, allocating
    /// nothing: the send path seals each payload straight into the frame it writes (#875).
    /// </summary>
    int SealInto(ReadOnlySpan<byte> plaintext, Span<byte> destination);

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
/// One thread at a time seals, and one thread at a time opens; concurrent seals, or concurrent opens, are the
/// caller's to prevent (#875). On the world server the connection's send thread seals and its read loop opens; on the
/// auth server its drain task seals and its read loop opens; a client serialises its own sends, and its own opens.
/// Each direction has its own <see cref="AesGcm"/>, so the two need no lock between them, and the session takes none.
/// A Debug build asserts that no two seals, and no two opens, overlap. Neither cipher is disposed: a send thread may
/// still seal for a connection that has just closed, and a disposed cipher would turn that into an exception on it.
/// Their native key handles are released, and the key material destroyed, by the handles' finalizers when the session
/// is collected.
/// </para>
/// <para>
/// A session goes through <see cref="Initialize"/> once, and only a session whose exchange
/// completed seals or opens anything (#855). An Initialize that throws (a peer key that does not
/// parse, say) leaves the session failed for good: it refuses a second Initialize, and every
/// Encrypt, SealInto and Decrypt, as an <see cref="InvalidOperationException"/>. So does a session whose send
/// counter is spent, since its next nonce would repeat one this key has already used. The two
/// public keys are readable only once the exchange has completed.
/// </para>
/// </remarks>
public class AvalonCryptoSession : IAvalonCryptoSession
{
    // New -> Initializing -> Ready, or Failed; Ready -> Exhausted. Ready is written only after both
    // ciphers are published, and Failed and Exhausted are never left, so a session reads Ready only
    // while it can seal and open. Checked once before each packet. Exhaustion is written only by the
    // one thread sealing, so no seal can pass the check once its counter is spent.
    private const int New = 0;
    private const int Initializing = 1;
    private const int Ready = 2;
    private const int Failed = 3;
    private const int Exhausted = 4;

    // Where the ciphertext starts in a sealed packet, after the nonce: the one place a plaintext may share with the
    // destination it is sealed into, since that is where the send path encodes it.
    private const int CiphertextOffset = SessionKeys.NonceSize;

    private volatile int _state;

    private readonly CryptoRole _role;
    private readonly AsymmetricCipherKeyPair _ownKeyPair;
    private ECPublicKeyParameters _otherEndPublicKey;
    private ECPublicKeyParameters _ownPublicKey;
    private byte[] _ownPublicKeyBytes;
    private byte[] _otherEndPublicKeyBytes;
    private readonly SecureRandom _secureRandom;
    private AesGcm? _sealer;
    private AesGcm? _opener;
    private readonly byte[] _sendNonce = new byte[SessionKeys.NonceSize];

#if DEBUG
    // Set while a seal, or an open, is under way: a second one that overlaps it is a caller breaking the one-sealer,
    // one-opener contract, and trips an assert. An in-use flag rather than an owning thread's id, since a sequential
    // caller (a drain task, a read loop) may resume on another pool thread after each await. Release builds have none.
    private int _sealing;
    private int _opening;
#endif

    public AvalonCryptoSession(CryptoRole role, AsymmetricCipherKeyPair? keyPair = null)
    {
        _state = New;
        Encryptor = Encrypt;
        _role = role;
        _secureRandom = new SecureRandom();
        _ownKeyPair = keyPair ?? AsymmetricCipher.GenerateECDHKeyPair(256);
    }

    public void Initialize(byte[] otherEndPublicKeyBytes)
    {
        // Claimed before anything else, so a second call, or a retry after a failed one, is
        // refused whatever happened to the first.
        if (Interlocked.CompareExchange(ref _state, Initializing, New) != New)
            throw new InvalidOperationException("Crypto session already initialized");

        byte[]? sharedSecret = null;
        byte[]? clientToServer = null;
        byte[]? serverToClient = null;
        AesGcm? sealer = null;
        AesGcm? opener = null;

        try
        {
            if (otherEndPublicKeyBytes == null || otherEndPublicKeyBytes.Length == 0)
                throw new ArgumentException("Invalid public key", nameof(otherEndPublicKeyBytes));

            // Parsed for the agreement, and kept verbatim for the salt. The salt is over the bytes the
            // two ends exchanged, so re-encoding here would make the derivation depend on this
            // library and the peer's agreeing on one canonical DER rather than on the transcript.
            _otherEndPublicKey = AsymmetricCipher.GetPublicKeyFromBytes(otherEndPublicKeyBytes);
            _otherEndPublicKeyBytes = (byte[])otherEndPublicKeyBytes.Clone();

            _ownPublicKey = AsymmetricCipher.GetPublicKeyFromKeyPair(_ownKeyPair);
            _ownPublicKeyBytes = AsymmetricCipher.GetPublicKeyBytes(_ownPublicKey);

            sharedSecret = AsymmetricCipher.CalculateSharedSecret(_ownKeyPair, _otherEndPublicKey);

            // Which array is the client's is the one thing the role decides here; the salt order
            // itself is fixed, so both ends build the same bytes.
            (clientToServer, serverToClient) = _role == CryptoRole.Client
                ? SessionKeys.Derive(sharedSecret, _ownPublicKeyBytes, _otherEndPublicKeyBytes)
                : SessionKeys.Derive(sharedSecret, _otherEndPublicKeyBytes, _ownPublicKeyBytes);

            // Keyed once, here, rather than per packet: re-keying GCM is most of a packet's cost. The
            // tag length is not fixed by the instance on this target; every call passes a
            // SessionKeys.TagSize span, which is what fixes it at 16 bytes.
            sealer = new AesGcm(_role == CryptoRole.Client ? clientToServer : serverToClient);
            opener = new AesGcm(_role == CryptoRole.Client ? serverToClient : clientToServer);

            // Both ciphers are written before the state: a volatile write publishes them, so a packet that reads Ready
            // sees both.
            _sealer = sealer;
            _opener = opener;
            _state = Ready;
        }
        catch
        {
            _state = Failed;
            sealer?.Dispose();
            opener?.Dispose();
            throw;
        }
        finally
        {
            // AesGcm imports the key, so these three are spent, and after a failure they are never
            // used. Best effort only: a moving GC may already have left copies elsewhere.
            if (sharedSecret != null) Array.Clear(sharedSecret, 0, sharedSecret.Length);
            if (clientToServer != null) Array.Clear(clientToServer, 0, clientToServer.Length);
            if (serverToClient != null) Array.Clear(serverToClient, 0, serverToClient.Length);
        }
    }

    /// <exception cref="InvalidOperationException">The exchange has not completed.</exception>
    public byte[] GetPublicKey()
    {
        if (!HasExchanged()) throw NotReady();
        return _ownPublicKeyBytes;
    }

    /// <exception cref="InvalidOperationException">The exchange has not completed.</exception>
    public byte[] GetOtherEndPublicKey()
    {
        if (!HasExchanged()) throw NotReady();
        return _otherEndPublicKeyBytes;
    }

    // The two public keys are set during Initialize and fixed once it completes, so they are
    // readable from Ready on, including after the send counter is spent: exhaustion ends what the
    // session may seal, not which exchange it was. Before that they are null or half-set.
    private bool HasExchanged()
    {
        int state = _state;
        return state == Ready || state == Exhausted;
    }

    public EncryptFunc Encryptor { get; }

    /// <exception cref="InvalidOperationException">
    /// The session never completed its exchange, or its send counter is spent.
    /// </exception>
    /// <exception cref="OverflowException">
    /// This call spent the send counter. Nothing is sealed, and the session is closed for good:
    /// every later call refuses rather than seal under a nonce this key has already used.
    /// </exception>
    public byte[] Encrypt(ReadOnlySpan<byte> data)
    {
        if (_state != Ready) throw NotReady();

        // A client's path (the load-test tool, the vectors): a new array per packet. The server seals into its frames.
        byte[] sealedPacket = new byte[SessionKeys.NonceSize + data.Length + SessionKeys.TagSize];
        SealInto(data, sealedPacket);
        return sealedPacket;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <paramref name="plaintext" /> may lie inside <paramref name="destination" /> only at the ciphertext's offset,
    /// byte 12, where it is sealed in place. Every refusal comes before the send counter moves, so none spends a nonce.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="plaintext" /> is too long to seal, <paramref name="destination" /> is shorter than the sealed
    /// packet, or the two overlap anywhere but at the ciphertext's offset.
    /// </exception>
    /// <exception cref="InvalidOperationException">As <see cref="Encrypt" />.</exception>
    /// <exception cref="OverflowException">As <see cref="Encrypt" />.</exception>
    public int SealInto(ReadOnlySpan<byte> plaintext, Span<byte> destination)
    {
        if (_state != Ready) throw NotReady();

        if (plaintext.Length > int.MaxValue - SessionKeys.NonceSize - SessionKeys.TagSize)
        {
            throw new ArgumentException("The plaintext is too long to seal", nameof(plaintext));
        }

        int sealedLength = SessionKeys.NonceSize + plaintext.Length + SessionKeys.TagSize;
        if (destination.Length < sealedLength)
        {
            throw new ArgumentException(
                $"A sealed packet of {sealedLength} bytes does not fit in {destination.Length}", nameof(destination));
        }

        // Anywhere else, the nonce would overwrite the plaintext before it is sealed, or the cipher would read bytes it
        // has already written; neither throws below, and the packet would be sealed wrong. The offset is the
        // plaintext's from the destination's start, so a plaintext that begins before it is refused too.
        if (((ReadOnlySpan<byte>)destination).Overlaps(plaintext, out int plaintextOffset)
            && plaintextOffset != CiphertextOffset)
        {
            throw new ArgumentException(
                "The plaintext may only alias the destination at its ciphertext offset", nameof(plaintext));
        }

        // Sliced before the counter moves, so that once it has, only the cipher itself can fail.
        Span<byte> nonce = destination.Slice(0, SessionKeys.NonceSize);
        Span<byte> ciphertext = destination.Slice(CiphertextOffset, plaintext.Length);
        Span<byte> tag = destination.Slice(CiphertextOffset + plaintext.Length, SessionKeys.TagSize);

#if DEBUG
        System.Diagnostics.Debug.Assert(Interlocked.Exchange(ref _sealing, 1) == 0,
            "Two seals overlapped on one session: one thread at a time seals (#875)");
#endif
        try
        {
            // The counter is the nonce. It is sent anyway, so a peer never has to track ours. It
            // is copied out and advanced before sealing, so no two packets can share it.
            _sendNonce.CopyTo(nonce);
            if (!SessionKeys.TryIncrementNonce(_sendNonce))
            {
                _state = Exhausted;
                throw new OverflowException("Session nonce counter exhausted");
            }

            _sealer!.Encrypt(nonce, plaintext, ciphertext, tag);
        }
        finally
        {
#if DEBUG
            Volatile.Write(ref _sealing, 0);
#endif
        }

        return sealedLength;
    }

    /// <exception cref="CryptographicException">
    /// The packet is shorter than a nonce and a tag, or it does not authenticate under this
    /// session's receiving key (on .NET 8 and later the platform throws the derived
    /// <c>AuthenticationTagMismatchException</c>, and clears what it had written to
    /// <paramref name="output"/>).
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The session never completed its exchange, or its send counter is spent.
    /// </exception>
    public int Decrypt(ReadOnlySpan<byte> data, byte[] output)
    {
        if (_state != Ready) throw NotReady();
        if (data.Length < SessionKeys.NonceSize + SessionKeys.TagSize)
        {
            throw new CryptographicException("Sealed packet is shorter than its nonce and tag");
        }

        int length = data.Length - SessionKeys.NonceSize - SessionKeys.TagSize;

#if DEBUG
        System.Diagnostics.Debug.Assert(Interlocked.Exchange(ref _opening, 1) == 0,
            "Two opens overlapped on one session: one thread at a time opens (#875)");
#endif
        try
        {
            _opener!.Decrypt(
                data.Slice(0, SessionKeys.NonceSize),
                data.Slice(SessionKeys.NonceSize, length),
                data.Slice(SessionKeys.NonceSize + length, SessionKeys.TagSize),
                output.AsSpan(0, length));
        }
        finally
        {
#if DEBUG
            Volatile.Write(ref _opening, 0);
#endif
        }

        return length;
    }

    // Off the packet path: built only when a packet is refused.
    private InvalidOperationException NotReady() => _state switch
    {
        Failed => new InvalidOperationException("Crypto session failed to initialize"),
        Exhausted => new InvalidOperationException("Crypto session nonce counter exhausted"),
        _ => new InvalidOperationException("Crypto session not initialized"),
    };

    public byte[] GenerateHandshakeData()
    {
        byte[] data = new byte[32];
        _secureRandom.NextBytes(data);
        return data;
    }
}
