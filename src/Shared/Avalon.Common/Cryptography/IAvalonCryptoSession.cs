using System;
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
/// </remarks>
public class AvalonCryptoSession : IAvalonCryptoSession
{
    private readonly object _lock = new object();

    private volatile bool _initialized;

    private readonly CryptoRole _role;
    private readonly AsymmetricCipherKeyPair _ownKeyPair;
    private ECPublicKeyParameters _otherEndPublicKey;
    private ECPublicKeyParameters _ownPublicKey;
    private IBufferedCipher _cipher;
    private byte[] _ownPublicKeyBytes;
    private byte[] _otherEndPublicKeyBytes;
    private readonly SecureRandom _secureRandom;
    private KeyParameter _sendKey;
    private KeyParameter _receiveKey;
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

        _sendKey = new KeyParameter(_role == CryptoRole.Client ? clientToServer : serverToClient);
        _receiveKey = new KeyParameter(_role == CryptoRole.Client ? serverToClient : clientToServer);

        // KeyParameter copies, so these three are spent. Best effort only: a moving GC may
        // already have left copies elsewhere.
        Array.Clear(sharedSecret, 0, sharedSecret.Length);
        Array.Clear(clientToServer, 0, clientToServer.Length);
        Array.Clear(serverToClient, 0, serverToClient.Length);

        _cipher = CipherUtilities.GetCipher("AES/GCM/NoPadding");
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

        lock (_lock)
        {
            // The counter is the nonce. It is sent anyway, so a peer never has to track ours.
            var nonce = (byte[])_sendNonce.Clone();
            SessionKeys.IncrementNonce(_sendNonce);

            var parameters = new ParametersWithIV(_sendKey, nonce);
            _cipher.Init(true, parameters);

            // Encrypt the data
            var ciphertext = _cipher.DoFinal(data.ToArray());

            _cipher.Reset();

            // Combine the nonce and ciphertext
            var encryptedData = new byte[nonce.Length + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, encryptedData, 0, nonce.Length);
            Buffer.BlockCopy(ciphertext, 0, encryptedData, nonce.Length, ciphertext.Length);

            return encryptedData;
        }
    }

    public int Decrypt(ReadOnlySpan<byte> data, byte[] output)
    {
        if (!_initialized) throw new InvalidOperationException("Crypto session not initialized");
        if (data.Length < SessionKeys.NonceSize) throw new CryptographicException("Sealed packet is shorter than its nonce");

        lock (_lock)
        {
            ReadOnlySpan<byte> nonce = data[..SessionKeys.NonceSize];
            byte[] ciphertext = data[SessionKeys.NonceSize..].ToArray(); // BouncyCastle 2.6.2 IBufferedCipher only provides byte[] overloads on netstandard2.0 — span input requires one copy here

            var parameters = new ParametersWithIV(_receiveKey, nonce.ToArray());
            _cipher.Init(false, parameters);

            int len = _cipher.ProcessBytes(ciphertext, 0, ciphertext.Length, output, 0);
            len += _cipher.DoFinal(output, len);
            _cipher.Reset();

            return len;
        }
    }

    public byte[] GenerateHandshakeData()
    {
        var data = new byte[32];
        _secureRandom.NextBytes(data);
        return data;
    }
}
