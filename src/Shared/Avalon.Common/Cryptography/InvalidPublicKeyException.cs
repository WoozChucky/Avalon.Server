using System.Security.Cryptography;

namespace Avalon.Common.Cryptography;

/// <summary>
/// A peer's public key is not one the exchange can use: not a DER SubjectPublicKeyInfo, not an EC key, not on P-256, the
/// point at infinity, or a point that is not on the curve (<see cref="AsymmetricCipher.GetPublicKeyFromBytes" />).
/// </summary>
/// <remarks>
/// Its own type because it is what the other end sends, not something the server got wrong: a session's
/// <c>Initialize</c> throws it for the peer's key, and a server can refuse that peer without treating the refusal as
/// a fault of its own. Any other exception from <c>Initialize</c> is not the peer's doing.
/// </remarks>
public sealed class InvalidPublicKeyException : CryptographicException
{
    public InvalidPublicKeyException()
        : base("Invalid public key")
    {
    }

    public InvalidPublicKeyException(string message)
        : base(message)
    {
    }

    public InvalidPublicKeyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
