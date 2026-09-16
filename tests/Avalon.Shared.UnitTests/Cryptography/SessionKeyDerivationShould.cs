// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Security.Cryptography;
using Avalon.Common.Cryptography;
using Org.BouncyCastle.Crypto;
using Xunit;

namespace Avalon.Shared.UnitTests.Cryptography;

/// <summary>
/// The v1 session derivation: HKDF over the ECDH secret, one key per direction, counter nonces.
/// </summary>
/// <remarks>
/// Both ends derive independently and never compare, so every disagreement surfaces as a packet
/// that will not open rather than as an error naming the cause. What these check is therefore not
/// only that the pieces work but that each one is load-bearing: swapping the two directions,
/// sharing one key between them, or leaving the nonce where it was each has a test that notices.
/// </remarks>
public class SessionKeyDerivationShould
{
    private sealed record Exchange(
        AsymmetricCipherKeyPair ClientKeyPair,
        byte[] ClientDer,
        AsymmetricCipherKeyPair ServerKeyPair,
        byte[] ServerDer,
        byte[] SharedSecret);

    private static Exchange NewExchange()
    {
        AsymmetricCipherKeyPair client = AsymmetricCipher.GenerateECDHKeyPair(256);
        AsymmetricCipherKeyPair server = AsymmetricCipher.GenerateECDHKeyPair(256);

        byte[] clientDer = AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(client));
        byte[] serverDer = AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(server));

        byte[] secret = AsymmetricCipher.CalculateSharedSecret(
            client, AsymmetricCipher.GetPublicKeyFromBytes(serverDer));

        return new Exchange(client, clientDer, server, serverDer, secret);
    }

    private static (IAvalonCryptoSession Client, IAvalonCryptoSession Server) Sessions(Exchange exchange)
    {
        var client = new AvalonCryptoSession(CryptoRole.Client, exchange.ClientKeyPair);
        client.Initialize(exchange.ServerDer);

        var server = new AvalonCryptoSession(CryptoRole.Server, exchange.ServerKeyPair);
        server.Initialize(exchange.ClientDer);

        return (client, server);
    }

    private static byte[] Open(byte[] sealedPacket, IAvalonCryptoSession session)
    {
        var output = new byte[sealedPacket.Length];
        int length = session.Decrypt(sealedPacket, output);
        return output[..length];
    }

    // -- the derivation ------------------------------------------------------

    /// <summary>
    /// Both directions must not share a key. Sharing one is what puts the two directions'
    /// nonces in one space and brings the GCM birthday bound back.
    /// </summary>
    [Fact]
    public void DeriveADifferentKeyForEachDirection()
    {
        Exchange exchange = NewExchange();

        (byte[] clientToServer, byte[] serverToClient) =
            SessionKeys.Derive(exchange.SharedSecret, exchange.ClientDer, exchange.ServerDer);

        Assert.Equal(32, clientToServer.Length);
        Assert.Equal(32, serverToClient.Length);
        Assert.NotEqual(clientToServer, serverToClient);
    }

    /// <summary>
    /// Neither derived key is the shared secret. The x-coordinate is KDF input, and using it
    /// directly is the finding this replaced.
    /// </summary>
    [Fact]
    public void NotHandBackTheSharedSecretAsAKey()
    {
        Exchange exchange = NewExchange();

        (byte[] clientToServer, byte[] serverToClient) =
            SessionKeys.Derive(exchange.SharedSecret, exchange.ClientDer, exchange.ServerDer);

        Assert.NotEqual(exchange.SharedSecret, clientToServer);
        Assert.NotEqual(exchange.SharedSecret, serverToClient);
    }

    /// <summary>
    /// The salt is the client's key then the server's, at both ends. An implementation that
    /// ordered it by ownership — mine, then theirs — would derive two different salts for one
    /// exchange and agree on nothing.
    /// </summary>
    [Fact]
    public void OrderTheSaltByRoleRatherThanByOwnership()
    {
        Exchange exchange = NewExchange();

        byte[] salt = SessionKeys.Salt(exchange.ClientDer, exchange.ServerDer);
        byte[] reversed = SessionKeys.Salt(exchange.ServerDer, exchange.ClientDer);

        Assert.Equal(exchange.ClientDer.Length + exchange.ServerDer.Length, salt.Length);
        Assert.Equal(exchange.ClientDer, salt[..exchange.ClientDer.Length]);
        Assert.Equal(exchange.ServerDer, salt[exchange.ClientDer.Length..]);
        Assert.NotEqual(salt, reversed);

        // And the order is not cosmetic: it reaches the keys.
        Assert.NotEqual(
            SessionKeys.Derive(exchange.SharedSecret, exchange.ClientDer, exchange.ServerDer).ClientToServer,
            SessionKeys.Derive(exchange.SharedSecret, exchange.ServerDer, exchange.ClientDer).ClientToServer);
    }

    /// <summary>
    /// The salt binds the keys to this exchange: a public key that differs by one bit derives a
    /// different pair, so a relay's substituted key cannot reach the keys either real end holds.
    /// </summary>
    [Fact]
    public void BindTheKeysToBothPublicKeys()
    {
        Exchange exchange = NewExchange();

        (byte[] clientToServer, byte[] serverToClient) =
            SessionKeys.Derive(exchange.SharedSecret, exchange.ClientDer, exchange.ServerDer);

        byte[] tampered = (byte[])exchange.ClientDer.Clone();
        tampered[^1] ^= 0x01;

        (byte[] otherClientToServer, byte[] otherServerToClient) =
            SessionKeys.Derive(exchange.SharedSecret, tampered, exchange.ServerDer);

        Assert.NotEqual(clientToServer, otherClientToServer);
        Assert.NotEqual(serverToClient, otherServerToClient);
    }

    /// <summary>
    /// The production derivation runs on BouncyCastle, because this assembly targets
    /// netstandard2.1 and <see cref="HKDF"/> arrived in .NET 5. Holding it to the platform's
    /// implementation is what says the label, the salt and the length are RFC 5869 HKDF-SHA256
    /// and not a near-miss of it — which is the form a C++ client will reach for.
    /// </summary>
    [Fact]
    public void MatchThePlatformHkdf()
    {
        Exchange exchange = NewExchange();
        byte[] salt = SessionKeys.Salt(exchange.ClientDer, exchange.ServerDer);

        (byte[] clientToServer, byte[] serverToClient) =
            SessionKeys.Derive(exchange.SharedSecret, exchange.ClientDer, exchange.ServerDer);

        Assert.Equal(
            HKDF.DeriveKey(HashAlgorithmName.SHA256, exchange.SharedSecret, 32, salt, SessionKeys.ClientToServerInfo()),
            clientToServer);

        Assert.Equal(
            HKDF.DeriveKey(HashAlgorithmName.SHA256, exchange.SharedSecret, 32, salt, SessionKeys.ServerToClientInfo()),
            serverToClient);
    }

    /// <summary>The labels are ASCII with no terminator, which is what a client has to type.</summary>
    [Fact]
    public void LabelTheDirectionsInAscii()
    {
        Assert.Equal("avalon/v1 c2s"u8.ToArray(), SessionKeys.ClientToServerInfo());
        Assert.Equal("avalon/v1 s2c"u8.ToArray(), SessionKeys.ServerToClientInfo());
    }

    // -- the two ends --------------------------------------------------------

    /// <summary>Both directions open at the far end, which is the whole contract.</summary>
    [Fact]
    public void CarryBothDirectionsBetweenAPairOfSessions()
    {
        (IAvalonCryptoSession client, IAvalonCryptoSession server) = Sessions(NewExchange());

        byte[] up = "client to server"u8.ToArray();
        byte[] down = "server to client"u8.ToArray();

        Assert.Equal(up, Open(client.Encrypt(up), server));
        Assert.Equal(down, Open(server.Encrypt(down), client));
    }

    /// <summary>
    /// A session cannot open what it sealed. This is the shape of the per-direction split: if one
    /// key were reused for both directions, or if send and receive were wired to the same one,
    /// this would succeed.
    /// </summary>
    [Fact]
    public void NotOpenItsOwnSealedPacket()
    {
        (IAvalonCryptoSession client, IAvalonCryptoSession server) = Sessions(NewExchange());

        byte[] sealedByClient = client.Encrypt("mine"u8);
        byte[] sealedByServer = server.Encrypt("mine"u8);

        Assert.ThrowsAny<Exception>(() => Open(sealedByClient, client));
        Assert.ThrowsAny<Exception>(() => Open(sealedByServer, server));
    }

    /// <summary>
    /// Two sessions of the same role derive the same send key, so a client sealing with the
    /// server's key — the mistake a swapped label makes — produces different bytes.
    /// </summary>
    [Fact]
    public void SealWithItsOwnDirectionsKey()
    {
        Exchange exchange = NewExchange();
        (IAvalonCryptoSession client, IAvalonCryptoSession server) = Sessions(exchange);

        (byte[] clientToServer, byte[] serverToClient) =
            SessionKeys.Derive(exchange.SharedSecret, exchange.ClientDer, exchange.ServerDer);

        byte[] plaintext = "which key sealed this"u8.ToArray();

        // Sealed independently of BouncyCastle, so agreement is not two copies of one mistake.
        Assert.Equal(PlatformSeal(clientToServer, SessionKeys.Nonce(0), plaintext), client.Encrypt(plaintext));
        Assert.Equal(PlatformSeal(serverToClient, SessionKeys.Nonce(0), plaintext), server.Encrypt(plaintext));
    }

    // -- the nonce -----------------------------------------------------------

    /// <summary>
    /// The counter advances per sealed packet. A nonce that stayed where it was repeats under one
    /// key, which is the GCM failure that hands over the authentication subkey.
    /// </summary>
    [Fact]
    public void AdvanceTheNonceOnEverySealedPacket()
    {
        (IAvalonCryptoSession client, _) = Sessions(NewExchange());

        byte[][] nonces =
        [
            client.Encrypt("one"u8)[..12],
            client.Encrypt("two"u8)[..12],
            client.Encrypt("three"u8)[..12],
        ];

        Assert.Equal(SessionKeys.Nonce(0), nonces[0]);
        Assert.Equal(SessionKeys.Nonce(1), nonces[1]);
        Assert.Equal(SessionKeys.Nonce(2), nonces[2]);
        Assert.Equal(3, nonces.Select(Convert.ToHexString).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The same plaintext seals to different bytes each time, which is what a repeating counter
    /// would stop doing — and the symptom nothing else in the pipeline would show.
    /// </summary>
    [Fact]
    public void SealTheSamePlaintextDifferentlyEachTime()
    {
        (IAvalonCryptoSession client, _) = Sessions(NewExchange());

        Assert.NotEqual(client.Encrypt("identical"u8), client.Encrypt("identical"u8));
    }

    /// <summary>
    /// The two directions count separately. They can afford to, because per-direction keys mean a
    /// shared counter value is not a shared nonce.
    /// </summary>
    [Fact]
    public void CountEachDirectionSeparately()
    {
        (IAvalonCryptoSession client, IAvalonCryptoSession server) = Sessions(NewExchange());

        client.Encrypt("one"u8);
        client.Encrypt("two"u8);

        Assert.Equal(SessionKeys.Nonce(0), server.Encrypt("first from the server"u8)[..12]);
    }

    /// <summary>
    /// Decryption reads the nonce that arrived rather than a counter of its own, so a packet
    /// still opens when it is not the one expected next.
    /// </summary>
    [Fact]
    public void OpenAPacketWhicheverOrderItArrivesIn()
    {
        (IAvalonCryptoSession client, IAvalonCryptoSession server) = Sessions(NewExchange());

        byte[] first = client.Encrypt("first"u8);
        byte[] second = client.Encrypt("second"u8);

        Assert.Equal("second"u8.ToArray(), Open(second, server));
        Assert.Equal("first"u8.ToArray(), Open(first, server));
    }

    /// <summary>
    /// Two sessions against one peer key pair seal differently, because the key differs even
    /// though both counters start at zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the cost of counter nonces, and the reason the server's key pair is per connection
    /// rather than per process. A counter from zero is only safe while no two sessions share a
    /// key: two do, and their first packets are sealed under one key and one nonce, which recovers
    /// the GCM authentication subkey for anyone who was listening. Random nonces were collision-safe
    /// whatever the peer did; a counter is not.
    /// </para>
    /// <para>
    /// The peer's key pair is the same in both sessions here, which is the far end's choice and not
    /// ours. What must differ is our own, so the derived key differs and the repeated counter value
    /// is not a repeated nonce. Note the limit of this case: it hands each session a fresh key pair
    /// itself, so it says what the derivation does and not where the pairs come from.
    /// ConnectionKeyPairShould in the World tests is what holds the wiring to giving each
    /// connection its own.
    /// </para>
    /// <para>
    /// Nothing else catches this. BouncyCastle refuses a repeated nonce per cipher instance, and
    /// each session owns its own, so its guard is silent across two of them.
    /// </para>
    /// </remarks>
    [Fact]
    public void NotSealUnderTheSameKeyAndNonceAsAnotherSession()
    {
        AsymmetricCipherKeyPair peer = AsymmetricCipher.GenerateECDHKeyPair(256);
        byte[] peerDer = AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(peer));

        // Two connections from one peer that reuses its key pair.
        var first = new AvalonCryptoSession(CryptoRole.Server, new CryptoManager().GetKeyPair());
        first.Initialize(peerDer);

        var second = new AvalonCryptoSession(CryptoRole.Server, new CryptoManager().GetKeyPair());
        second.Initialize(peerDer);

        byte[] a = first.Encrypt("connection one"u8);
        byte[] b = second.Encrypt("connection two"u8);

        // The counter is per session, so both nonces ARE zero. That is fine only because the keys
        // differ, which is what this actually checks — through the one thing a test can observe.
        Assert.Equal(SessionKeys.Nonce(0), a[..12]);
        Assert.Equal(SessionKeys.Nonce(0), b[..12]);

        // Each opens at its own peer session and not at the other's.
        var peerOfFirst = new AvalonCryptoSession(CryptoRole.Client, peer);
        peerOfFirst.Initialize(first.GetPublicKey());

        var peerOfSecond = new AvalonCryptoSession(CryptoRole.Client, peer);
        peerOfSecond.Initialize(second.GetPublicKey());

        Assert.Equal("connection one"u8.ToArray(), Open(a, peerOfFirst));
        Assert.ThrowsAny<Exception>(() => Open(b, peerOfFirst));
    }

    /// <summary>
    /// And the keys themselves differ, stated directly rather than only through a packet that
    /// fails to open — so a reader knows which property the case above rests on.
    /// </summary>
    [Fact]
    public void DeriveADifferentKeyForEachConnectionFromOnePeerKey()
    {
        AsymmetricCipherKeyPair peer = AsymmetricCipher.GenerateECDHKeyPair(256);
        byte[] peerDer = AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(peer));

        byte[][] keys = new byte[2][];

        for (int i = 0; i < 2; i++)
        {
            AsymmetricCipherKeyPair ours = new CryptoManager().GetKeyPair();
            byte[] ourDer = AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(ours));

            byte[] secret = AsymmetricCipher.CalculateSharedSecret(
                ours, AsymmetricCipher.GetPublicKeyFromBytes(peerDer));

            keys[i] = SessionKeys.Derive(secret, peerDer, ourDer).ServerToClient;
        }

        Assert.NotEqual(keys[0], keys[1]);
    }

    /// <summary>96 bits, big-endian, from zero — the number a client has to reproduce.</summary>
    [Theory]
    [InlineData(0UL, "000000000000000000000000")]
    [InlineData(1UL, "000000000000000000000001")]
    [InlineData(255UL, "0000000000000000000000ff")]
    [InlineData(256UL, "000000000000000000000100")]
    [InlineData(ulong.MaxValue, "00000000ffffffffffffffff")]
    public void CountTheNonceBigEndian(ulong counter, string expected)
    {
        Assert.Equal(expected, Convert.ToHexString(SessionKeys.Nonce(counter)).ToLowerInvariant());
    }

    /// <summary>The carry reaches the byte above it rather than wrapping one byte in place.</summary>
    [Fact]
    public void CarryTheNonceAcrossAByteBoundary()
    {
        byte[] nonce = SessionKeys.Nonce(255);
        SessionKeys.IncrementNonce(nonce);

        Assert.Equal(SessionKeys.Nonce(256), nonce);
    }

    /// <summary>
    /// An exhausted counter fails rather than wrapping to a nonce already used. Unreachable at
    /// 2^96 packets; the alternative is silent forgeable traffic.
    /// </summary>
    [Fact]
    public void RefuseToWrapAnExhaustedCounter()
    {
        byte[] nonce = Enumerable.Repeat((byte)0xff, 12).ToArray();

        Assert.Throws<OverflowException>(() => SessionKeys.IncrementNonce(nonce));
    }

    // -- the wire shape ------------------------------------------------------

    /// <summary>
    /// The nonce is still transmitted, so the packet format did not change with the derivation:
    /// 12 bytes of nonce, the ciphertext, then a 16-byte tag.
    /// </summary>
    [Fact]
    public void KeepTheNonceOnTheWire()
    {
        (IAvalonCryptoSession client, _) = Sessions(NewExchange());

        byte[] plaintext = new byte[37];
        byte[] sealedPacket = client.Encrypt(plaintext);

        Assert.Equal(12 + plaintext.Length + 16, sealedPacket.Length);
        Assert.Equal(SessionKeys.Nonce(0), sealedPacket[..12]);
    }

    /// <summary>A packet too short to hold a nonce is refused rather than read past its end.</summary>
    [Fact]
    public void RefuseAPacketShorterThanItsNonce()
    {
        (IAvalonCryptoSession client, _) = Sessions(NewExchange());

        Assert.Throws<CryptographicException>(() => client.Decrypt(new byte[11], new byte[64]));
    }

    private static byte[] PlatformSeal(byte[] key, byte[] nonce, byte[] plaintext)
    {
        using var aes = new AesGcm(key, 16);

        var result = new byte[nonce.Length + plaintext.Length + 16];
        nonce.CopyTo(result.AsSpan());

        aes.Encrypt(
            nonce,
            plaintext,
            result.AsSpan(nonce.Length, plaintext.Length),
            result.AsSpan(nonce.Length + plaintext.Length, 16));

        return result;
    }
}
