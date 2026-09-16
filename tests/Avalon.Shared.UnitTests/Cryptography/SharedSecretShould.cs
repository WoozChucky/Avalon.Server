// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using Avalon.Common.Cryptography;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Xunit;
using Xunit.Abstractions;

namespace Avalon.Shared.UnitTests.Cryptography;

/// <summary>
/// The session key is the ECDH shared secret, and a P-256 x-coordinate is a 256-bit number — which
/// means roughly one in 256 of them has a zero in the top byte. An encoding that drops leading zeros
/// then yields 31 bytes where AES-256 requires 32, and the connection that drew it fails on its first
/// sealed packet rather than at the handshake, which is what makes it look like a fluke.
///
/// These are deterministic: the key pairs come from a seeded generator, so the pair that trips it is
/// the same pair on every machine and every run.
/// </summary>
public class SharedSecretShould
{
    private const int RequiredKeyBytes = 32;   // AES-256

    private readonly ITestOutputHelper _output;

    public SharedSecretShould(ITestOutputHelper output) => _output = output;

    private static SecureRandom SeededRandom()
    {
        var random = SecureRandom.GetInstance("SHA256PRNG", autoSeed: false);
        random.SetSeed("avalon shared-secret leading zero"u8.ToArray());
        return random;
    }

    private static AsymmetricCipherKeyPair GeneratePair(SecureRandom random)
    {
        var generator = GeneratorUtilities.GetKeyPairGenerator("ECDH");
        generator.Init(new ECKeyGenerationParameters(SecObjectIdentifiers.SecP256r1, random));
        return generator.GenerateKeyPair();
    }

    /// <summary>
    ///     Whether this exchange's x-coordinate has a zero top byte — asked of the agreement itself
    ///     rather than of the code under test, which is the whole point: once the encoding is fixed
    ///     the code stops reporting short, while the coordinates that caused it keep arising.
    /// </summary>
    private static bool HasLeadingZero(AsymmetricCipherKeyPair ours, ECPublicKeyParameters theirs)
    {
        var agreement = new ECDHBasicAgreement();
        agreement.Init(ours.Private);
        return agreement.CalculateAgreement(theirs).ToByteArrayUnsigned().Length < RequiredKeyBytes;
    }

    /// <summary>The first exchange whose x-coordinate carries a leading zero, and how many it took.</summary>
    private static (AsymmetricCipherKeyPair Ours, ECPublicKeyParameters Theirs, int Attempts) FindShortSecret(
        int giveUpAfter = 20_000)
    {
        SecureRandom random = SeededRandom();
        for (int attempt = 1; attempt <= giveUpAfter; attempt++)
        {
            AsymmetricCipherKeyPair ours = GeneratePair(random);
            AsymmetricCipherKeyPair theirs = GeneratePair(random);
            var theirPublic = (ECPublicKeyParameters)theirs.Public;

            if (HasLeadingZero(ours, theirPublic))
                return (ours, theirPublic, attempt);
        }

        throw new InvalidOperationException(
            $"No leading-zero x-coordinate in {giveUpAfter} exchanges, which at the expected rate is " +
            "vanishingly unlikely — suspect the search rather than the curve.");
    }

    [Fact]
    public void AriseAboutOnceIn256Exchanges()
    {
        // The rate, measured rather than predicted: a leading zero byte in a uniformly distributed
        // 256-bit x-coordinate is a 1-in-256 event, so a few thousand exchanges should show a few
        // dozen. The band is wide because this is a sanity check on the mechanism, not on the RNG.
        const int Exchanges = 5_000;
        SecureRandom random = SeededRandom();
        int shortSecrets = 0;

        for (int i = 0; i < Exchanges; i++)
        {
            AsymmetricCipherKeyPair ours = GeneratePair(random);
            AsymmetricCipherKeyPair theirs = GeneratePair(random);
            if (HasLeadingZero(ours, (ECPublicKeyParameters)theirs.Public))
                shortSecrets++;
        }

        _output.WriteLine($"{shortSecrets} of {Exchanges} exchanges drew an x-coordinate with a zero " +
                          $"top byte (1 in {(double)Exchanges / Math.Max(shortSecrets, 1):F0}).");

        Assert.InRange(shortSecrets, Exchanges / 512, Exchanges / 128);
    }

    [Fact]
    public void BeAFullLengthKey_EvenWhenTheXCoordinateHasALeadingZero()
    {
        (AsymmetricCipherKeyPair ours, ECPublicKeyParameters theirs, int attempts) = FindShortSecret();
        _output.WriteLine($"Reached a short secret on exchange {attempts}.");

        byte[] secret = AsymmetricCipher.CalculateSharedSecret(ours, theirs);

        Assert.Equal(RequiredKeyBytes, secret.Length);
    }

    [Fact]
    public void SealAPacket_OnAnExchangeThatEncodedShort()
    {
        // The consequence, and the reason this reads as intermittent rather than broken: the handshake
        // completes and the failure lands on the first packet anyone tries to seal.
        (AsymmetricCipherKeyPair ours, ECPublicKeyParameters theirs, _) = FindShortSecret();

        var session = new AvalonCryptoSession(ours);
        session.Initialize(AsymmetricCipher.GetPublicKeyBytes(theirs));

        byte[] sealed_ = session.Encrypt("a packet"u8);

        Assert.NotEmpty(sealed_);
    }
}
