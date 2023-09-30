using System.Security.Cryptography;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Avalon.Common.Cryptography;

public class AsymmetricCipher
{
    public static AsymmetricCipherKeyPair GenerateECDHKeyPair()
    {
        // Generate an ECDH key pair using the Bouncy Castle library
        var keyGenParams = new ECKeyGenerationParameters(SecObjectIdentifiers.SecP256r1, new SecureRandom());
        var keyPairGen = GeneratorUtilities.GetKeyPairGenerator("ECDH");
        keyPairGen.Init(keyGenParams);

        return keyPairGen.GenerateKeyPair();
    }

    public static ECPublicKeyParameters GetPublicKeyFromKeyPair(AsymmetricCipherKeyPair keyPair)
    {
        if (keyPair == null) throw new ArgumentNullException(nameof(keyPair));
        return (ECPublicKeyParameters)keyPair.Public;
    }
    
    public static ECPublicKeyParameters GetPrivateKeyFromKeyPair(AsymmetricCipherKeyPair keyPair)
    {
        if (keyPair == null) throw new ArgumentNullException(nameof(keyPair));
        return (ECPublicKeyParameters)keyPair.Private;
    }
    
    public static byte[] GetPublicKeyBytes(ECPublicKeyParameters publicKey)
    {
        var publicKeyInfo = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKey);
        return publicKeyInfo.GetDerEncoded();
    }
    
    public static ECPublicKeyParameters GetPublicKeyFromBytes(byte[] publicKeyBytes)
    {
        // Parse the byte array to reconstruct the public key
        var asn1Object = Asn1Object.FromByteArray(publicKeyBytes);
        var publicKeyInfo = SubjectPublicKeyInfo.GetInstance(asn1Object);
        var publicKeyParameter = PublicKeyFactory.CreateKey(publicKeyInfo);

        // Cast the public key to ECPublicKeyParameters
        if (publicKeyParameter is ECPublicKeyParameters publicKey)
        {
            return publicKey;
        }

        throw new CryptographicException("Invalid public key");
    }
    
    public static byte[] CalculateSharedSecret(AsymmetricCipherKeyPair ownKeyPair, ECPublicKeyParameters otherPublicKey)
    {
        // Create an ECDH key agreement object
        var agreement = new ECDHBasicAgreement();

        // Initialize the agreement object with it's own private key
        agreement.Init(ownKeyPair.Private);

        // Calculate the shared secret using the other end's public key
        var secret = agreement.CalculateAgreement(otherPublicKey);

        // Convert the shared secret to a byte array
        byte[] sharedSecret = secret.ToByteArray();

        return sharedSecret;
    }
}