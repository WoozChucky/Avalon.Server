// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.Security.Cryptography;
using Avalon.Common.Cryptography;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Xunit;

namespace Avalon.Shared.UnitTests.Cryptography;

/// <summary>
/// The peer's public key arrives as DER over a socket, which lets the peer choose the curve.
/// </summary>
/// <remarks>
/// BouncyCastle probably rejects most of these inside the agreement already. That is the reason
/// to assert it here rather than the reason not to: the safety would otherwise rest on
/// undocumented library behaviour that a version bump can change, with nothing local saying it is
/// relied on.
/// </remarks>
public class PublicKeyValidationShould
{
    private static byte[] PublicKeyOn(DerObjectIdentifier curve)
    {
        var generator = GeneratorUtilities.GetKeyPairGenerator("ECDH");
        generator.Init(new ECKeyGenerationParameters(curve, new SecureRandom()));
        AsymmetricCipherKeyPair pair = generator.GenerateKeyPair();

        return SubjectPublicKeyInfoFactory
            .CreateSubjectPublicKeyInfo((ECPublicKeyParameters)pair.Public)
            .GetDerEncoded();
    }

    [Fact]
    public void AcceptAP256Key()
    {
        byte[] der = PublicKeyOn(SecObjectIdentifiers.SecP256r1);

        ECPublicKeyParameters key = AsymmetricCipher.GetPublicKeyFromBytes(der);

        Assert.False(key.Q.IsInfinity);
        Assert.Equal(der, AsymmetricCipher.GetPublicKeyBytes(key));
    }

    /// <summary>
    /// A key on another curve agrees a secret of a different size and a different value, and the
    /// failure would otherwise land on the first sealed packet rather than on the key.
    /// </summary>
    [Theory]
    [InlineData("secp384r1")]
    [InlineData("secp521r1")]
    [InlineData("secp256k1")]
    [InlineData("secp224r1")]
    public void RefuseAKeyOnAnotherCurve(string curve)
    {
        byte[] der = PublicKeyOn(new DerObjectIdentifier(SecNamedCurves.GetOid(curve).Id));

        Assert.Throws<CryptographicException>(() => AsymmetricCipher.GetPublicKeyFromBytes(der));
    }

    [Fact]
    public void RefuseAKeyThatIsNotEllipticCurve()
    {
        var generator = GeneratorUtilities.GetKeyPairGenerator("RSA");
        generator.Init(new KeyGenerationParameters(new SecureRandom(), 1024));

        byte[] der = SubjectPublicKeyInfoFactory
            .CreateSubjectPublicKeyInfo(generator.GenerateKeyPair().Public)
            .GetDerEncoded();

        Assert.Throws<CryptographicException>(() => AsymmetricCipher.GetPublicKeyFromBytes(der));
    }

    /// <summary>
    /// A point off the curve. The coordinates are the right shape and the wrong pair, which is
    /// the invalid-curve attack's opening move.
    /// </summary>
    [Fact]
    public void RefuseAPointThatIsNotOnTheCurve()
    {
        byte[] der = PublicKeyOn(SecObjectIdentifiers.SecP256r1);

        // The last byte is the low byte of Y. Changing it leaves a well-formed encoding of a
        // point that does not satisfy the curve equation.
        der[^1] ^= 0x01;

        Assert.Throws<CryptographicException>(() => AsymmetricCipher.GetPublicKeyFromBytes(der));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { 0x30, 0x59, 0xff, 0xff })]
    public void RefuseBytesThatAreNotASubjectPublicKeyInfo(byte[] der)
    {
        Assert.Throws<CryptographicException>(() => AsymmetricCipher.GetPublicKeyFromBytes(der));
    }

    [Fact]
    public void RefuseNothingAtAll()
    {
        Assert.Throws<CryptographicException>(() => AsymmetricCipher.GetPublicKeyFromBytes(null!));
    }

    /// <summary>
    /// And the session refuses it, rather than reporting the failure a step later.
    /// </summary>
    /// <remarks>
    /// It refuses the key, not the session: Initialize marks itself initialized before it
    /// validates, so a session whose Initialize threw will fail inside Encrypt on a null key
    /// rather than report that it was never initialized. Unreachable — the read loop's catch-all
    /// closes the connection on the throw — and left alone because reordering the flag changes
    /// what a second Initialize does, which is a separate contract.
    /// </remarks>
    [Fact]
    public void RefuseAForeignCurveAtTheSession()
    {
        var session = new AvalonCryptoSession(CryptoRole.Server);

        Assert.Throws<CryptographicException>(
            () => session.Initialize(PublicKeyOn(SecObjectIdentifiers.SecP384r1)));
    }
}
