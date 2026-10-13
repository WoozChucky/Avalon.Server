namespace Avalon.Common.Cryptography;

/// <summary>
/// A session was asked to seal or open, or for a public key, before its exchange completed: it has no keys yet.
/// </summary>
/// <remarks>
/// Its own type because, of the reasons a session refuses, this is the one the other end can cause (#897): a peer
/// that flags a packet Encrypted before it has exchanged keys makes the receiver's session refuse this way. A
/// session whose exchange failed, or whose send counter is spent, refuses with a plain
/// <see cref="InvalidOperationException" />: neither is reached by what a peer sends on a healthy connection.
/// </remarks>
public sealed class CryptoSessionNotInitializedException : InvalidOperationException
{
    public CryptoSessionNotInitializedException()
        : base("Crypto session not initialized")
    {
    }

    public CryptoSessionNotInitializedException(string message)
        : base(message)
    {
    }

    public CryptoSessionNotInitializedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
