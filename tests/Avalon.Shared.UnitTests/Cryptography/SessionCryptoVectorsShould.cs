// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Avalon.Common.Cryptography;
using Avalon.SchemaGen;
using Avalon.Shared.UnitTests.Schema;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.EC;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Xunit;

namespace Avalon.Shared.UnitTests.Cryptography;

/// <summary>
/// Holds the checked-in known-answer vectors to the crypto that produced them.
/// </summary>
/// <remarks>
/// The vectors exist because the client and the server derive the same two keys without ever
/// comparing them: a derivation that disagrees fails as a packet that will not open, on a live
/// connection, with nothing on the wire naming the cause. The file lets either end check itself
/// offline. These tests are the other half — the file is only an answer key while something holds
/// the implementation to it, and reading the checked-in bytes rather than regenerating them is
/// what makes the artifact a client is handed the thing under test.
/// </remarks>
public class SessionCryptoVectorsShould
{
    private const string RegenerateCommand = "dotnet run --project tools/Avalon.SchemaGen";

    private static string Path_ =>
        Path.Combine(RepositoryLayout.Root(), "schema", SessionCryptoVectors.DirectoryName, SessionCryptoVectors.FileName);

    private static IReadOnlyList<CryptoExchange> CheckedIn()
    {
        Assert.True(
            File.Exists(Path_),
            $"schema/{SessionCryptoVectors.DirectoryName}/{SessionCryptoVectors.FileName} is missing. Regenerate it with:"
            + $"{Environment.NewLine}    {RegenerateCommand}");

        IReadOnlyList<CryptoExchange> exchanges = SessionCryptoVectors.Parse(File.ReadAllText(Path_));

        Assert.NotEmpty(exchanges);

        return exchanges;
    }

    public static TheoryData<string> Names()
    {
        TheoryData<string> data = [];
        foreach (CryptoExchange exchange in CheckedIn()) data.Add(exchange.Name);
        return data;
    }

    private static CryptoExchange Named(string name) =>
        CheckedIn().Single(exchange => string.Equals(exchange.Name, name, StringComparison.Ordinal));

    /// <summary>A key pair rebuilt from a private scalar in the file, exactly as a peer would hold it.</summary>
    private static AsymmetricCipherKeyPair KeyPair(byte[] scalar)
    {
        var domain = new ECNamedDomainParameters(
            SecObjectIdentifiers.SecP256r1, CustomNamedCurves.GetByName("secp256r1"));

        var d = new BigInteger(1, scalar);

        return new AsymmetricCipherKeyPair(
            new ECPublicKeyParameters("ECDH", domain.G.Multiply(d).Normalize(), domain),
            new ECPrivateKeyParameters("ECDH", d, domain));
    }

    // -- the derivation, value by value --------------------------------------

    /// <summary>
    /// Every derived value in the file, from the two private scalars in it and nothing else. A
    /// client with the scalars can walk the same path and find where it diverges.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void BeWhatTheImplementationDerivesFromTheScalarsInIt(string name)
    {
        CryptoExchange vector = Named(name);

        AsymmetricCipherKeyPair client = KeyPair(vector.ClientPrivateScalar);
        AsymmetricCipherKeyPair server = KeyPair(vector.ServerPrivateScalar);

        Assert.Equal(
            vector.ClientPublicKeyDer,
            AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(client)));

        Assert.Equal(
            vector.ServerPublicKeyDer,
            AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(server)));

        // Agreed from either side, because it is the same number and a client reaches it from the
        // other direction than the server does.
        byte[] fromClient = AsymmetricCipher.CalculateSharedSecret(
            client, AsymmetricCipher.GetPublicKeyFromBytes(vector.ServerPublicKeyDer));

        byte[] fromServer = AsymmetricCipher.CalculateSharedSecret(
            server, AsymmetricCipher.GetPublicKeyFromBytes(vector.ClientPublicKeyDer));

        Assert.Equal(vector.SharedSecret, fromClient);
        Assert.Equal(vector.SharedSecret, fromServer);
        Assert.Equal(32, vector.SharedSecret.Length);

        Assert.Equal(
            vector.Salt,
            SessionKeys.Salt(vector.ClientPublicKeyDer, vector.ServerPublicKeyDer));

        (byte[] clientToServer, byte[] serverToClient) =
            SessionKeys.Derive(vector.SharedSecret, vector.ClientPublicKeyDer, vector.ServerPublicKeyDer);

        Assert.Equal(vector.ClientToServer, clientToServer);
        Assert.Equal(vector.ServerToClient, serverToClient);
    }

    /// <summary>
    /// The two keys in the file are HKDF-SHA256 by the platform's own implementation, so a client
    /// reaching for mbedTLS or OpenSSL is reading an RFC 5869 answer and not a BouncyCastle one.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void MatchThePlatformHkdf(string name)
    {
        CryptoExchange vector = Named(name);

        Assert.Equal(
            HKDF.DeriveKey(HashAlgorithmName.SHA256, vector.SharedSecret, 32, vector.Salt, "avalon/v1 c2s"u8.ToArray()),
            vector.ClientToServer);

        Assert.Equal(
            HKDF.DeriveKey(HashAlgorithmName.SHA256, vector.SharedSecret, 32, vector.Salt, "avalon/v1 s2c"u8.ToArray()),
            vector.ServerToClient);
    }

    /// <summary>
    /// The two directions are keyed differently, in the artifact and not only in the code. A file
    /// exported from an implementation that had collapsed them would read as agreement.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void KeyTheTwoDirectionsDifferently(string name)
    {
        CryptoExchange vector = Named(name);

        Assert.NotEqual(vector.ClientToServer, vector.ServerToClient);
        Assert.NotEqual(vector.SharedSecret, vector.ClientToServer);
        Assert.NotEqual(vector.SharedSecret, vector.ServerToClient);
    }

    // -- the sealed packets --------------------------------------------------

    /// <summary>
    /// Each recorded packet, sealed again by a session of the right role in the recorded order.
    /// This is where a swapped label or a stalled counter shows: the round trip still works with
    /// either mistake, and these bytes do not.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void SealEachPacketExactlyAsRecorded(string name)
    {
        CryptoExchange vector = Named(name);

        var client = new AvalonCryptoSession(CryptoRole.Client, KeyPair(vector.ClientPrivateScalar));
        client.Initialize(vector.ServerPublicKeyDer);

        var server = new AvalonCryptoSession(CryptoRole.Server, KeyPair(vector.ServerPrivateScalar));
        server.Initialize(vector.ClientPublicKeyDer);

        foreach (CryptoPacket packet in vector.Packets)
        {
            IAvalonCryptoSession sender = string.Equals(packet.Direction, "c2s", StringComparison.Ordinal)
                ? client
                : server;

            byte[] resealed = sender.Encrypt(packet.Plaintext);

            Assert.Equal(packet.Nonce, resealed[..SessionKeys.NonceSize]);
            Assert.Equal(packet.Ciphertext, resealed[SessionKeys.NonceSize..]);
        }
    }

    /// <summary>
    /// And each one opens at the other end, from the bytes in the file rather than from bytes
    /// this test produced — which is the direction a client's own vectors will be checked in.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void OpenEachRecordedPacketAtTheOtherEnd(string name)
    {
        CryptoExchange vector = Named(name);

        var client = new AvalonCryptoSession(CryptoRole.Client, KeyPair(vector.ClientPrivateScalar));
        client.Initialize(vector.ServerPublicKeyDer);

        var server = new AvalonCryptoSession(CryptoRole.Server, KeyPair(vector.ServerPrivateScalar));
        server.Initialize(vector.ClientPublicKeyDer);

        foreach (CryptoPacket packet in vector.Packets)
        {
            IAvalonCryptoSession receiver = string.Equals(packet.Direction, "c2s", StringComparison.Ordinal)
                ? server
                : client;

            byte[] wire = [.. packet.Nonce, .. packet.Ciphertext];
            var output = new byte[wire.Length];

            int length = receiver.Decrypt(wire, output);

            Assert.Equal(packet.Plaintext, output[..length]);
        }
    }

    /// <summary>
    /// Sealed under the key the file names, checked with the platform's AES-GCM. A client that
    /// derived both keys correctly and then sealed with the wrong one is the failure this
    /// separates out from a bad derivation.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void SealEachDirectionUnderItsOwnKey(string name)
    {
        CryptoExchange vector = Named(name);

        foreach (CryptoPacket packet in vector.Packets)
        {
            byte[] key = string.Equals(packet.Direction, "c2s", StringComparison.Ordinal)
                ? vector.ClientToServer
                : vector.ServerToClient;

            using var aes = new AesGcm(key, 16);

            var ciphertext = new byte[packet.Plaintext.Length];
            var tag = new byte[16];

            aes.Encrypt(packet.Nonce, packet.Plaintext, ciphertext, tag);

            byte[] expected = [.. ciphertext, .. tag];
            Assert.Equal(packet.Ciphertext, expected);
        }
    }

    // -- what the file is required to cover ----------------------------------

    /// <summary>
    /// Each direction counts from zero and advances by one. A file whose nonces repeated would
    /// agree with an implementation that never advanced its counter.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void CountEachDirectionFromZero(string name)
    {
        CryptoExchange vector = Named(name);

        foreach (string direction in new[] { "c2s", "s2c" })
        {
            CryptoPacket[] packets = vector.Packets
                .Where(packet => string.Equals(packet.Direction, direction, StringComparison.Ordinal))
                .ToArray();

            Assert.True(packets.Length >= 2, $"{name} has {packets.Length} {direction} packets; a stalled counter needs two to show.");

            for (int i = 0; i < packets.Length; i++)
            {
                Assert.Equal((ulong)i, packets[i].Counter);
                Assert.Equal(SessionKeys.Nonce((ulong)i), packets[i].Nonce);
            }
        }
    }

    /// <summary>
    /// One exchange whose shared x-coordinate has a zero top byte. It is about one exchange in
    /// 256, so an implementation that encodes the secret minimally passes every other vector here
    /// and fails in production at that rate.
    /// </summary>
    [Fact]
    public void CoverASharedSecretWithALeadingZero()
    {
        Assert.Contains(CheckedIn(), exchange => exchange.SharedSecret[0] == 0x00);
    }

    /// <summary>An empty plaintext seals to a bare tag, which is the length a reader gets wrong.</summary>
    [Fact]
    public void CoverAnEmptyPlaintext()
    {
        Assert.Contains(
            CheckedIn().SelectMany(exchange => exchange.Packets),
            packet => packet.Plaintext.Length == 0 && packet.Ciphertext.Length == 16);
    }

    /// <summary>
    /// The wire shape the derivation did not change: nonce, ciphertext, 16-byte tag, and the
    /// ciphertext the same length as its plaintext.
    /// </summary>
    [Fact]
    public void RecordThePacketShapeTheWireStillUses()
    {
        foreach (CryptoPacket packet in CheckedIn().SelectMany(exchange => exchange.Packets))
        {
            Assert.Equal(SessionKeys.NonceSize, packet.Nonce.Length);
            Assert.Equal(packet.Plaintext.Length + 16, packet.Ciphertext.Length);
        }
    }

    /// <summary>
    /// The exported SubjectPublicKeyInfo is the named-curve form the handshake's length check
    /// requires, and is canonical — re-encoding a parsed copy gives the same bytes. That is what
    /// lets the salt be built from the DER as it arrived at one end and as it was written at the
    /// other.
    /// </summary>
    [Fact]
    public void CarryCanonicalNamedCurvePublicKeys()
    {
        int expected = new CryptoManager().GetValidKeySize();

        foreach (CryptoExchange exchange in CheckedIn())
        {
            foreach (byte[] der in new[] { exchange.ClientPublicKeyDer, exchange.ServerPublicKeyDer })
            {
                Assert.Equal(expected, der.Length);
                Assert.Equal(der, AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromBytes(der)));
            }
        }
    }

    // -- drift ---------------------------------------------------------------

    /// <summary>
    /// The checked-in text against a fresh export. A change in what the server derives has to
    /// arrive here as a diff someone reads, not as a vendored file that quietly stopped matching.
    /// </summary>
    [Fact]
    public void MatchTheExportItCameFrom()
    {
        string[] checkedIn = Lines(File.ReadAllText(Path_));
        string[] regenerated = Lines(SessionCryptoVectors.Generate());

        if (checkedIn.SequenceEqual(regenerated, StringComparer.Ordinal))
        {
            return;
        }

        int shared = Math.Min(checkedIn.Length, regenerated.Length);
        int first = 0;

        while (first < shared && string.Equals(checkedIn[first], regenerated[first], StringComparison.Ordinal))
        {
            first++;
        }

        Assert.Fail(string.Join(
            Environment.NewLine,
            $"schema/{SessionCryptoVectors.DirectoryName}/{SessionCryptoVectors.FileName} no longer matches the export it came from.",
            string.Empty,
            "If the derivation changed on purpose, re-export the vectors, commit them, and change the client to match:",
            $"    {RegenerateCommand}",
            string.Empty,
            $"First difference at line {(first + 1).ToString(CultureInfo.InvariantCulture)}.",
            $"    < {At(checkedIn, first)}      (checked in)",
            $"    > {At(regenerated, first)}      (exported)"));
    }

    private static string At(string[] lines, int index) => index < lines.Length ? lines[index] : "<end of file>";

    // Compares content rather than encoding: a working tree checked out with CRLF is not drift.
    private static string[] Lines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
}
