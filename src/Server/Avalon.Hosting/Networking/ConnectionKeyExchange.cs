using Avalon.Common.Cryptography;

namespace Avalon.Hosting.Networking;

/// <summary>
/// A connection's half of the session key exchange: a P-256 key pair of its own and the session that is agreed on it
/// once the peer's public key arrives.
/// </summary>
/// <remarks>
/// Per connection, not per process. The session nonce is a counter from zero, so two connections that agreed the same
/// key would seal their first packets under the same key and the same nonce, which recovers the GCM authentication key
/// for anyone listening. A shared server key pair made that reachable from the far end alone, by a client that reused
/// its own key pair, and neither end could see it happen.
/// </remarks>
public sealed class ConnectionKeyExchange
{
    public ConnectionKeyExchange()
    {
        ServerCrypto = new CryptoManager();
        Session = new AvalonCryptoSession(CryptoRole.Server, ServerCrypto.GetKeyPair());
    }

    /// <summary>The connection's own key pair, whose public key the server sends the peer.</summary>
    public ICryptoManager ServerCrypto { get; }

    /// <summary>The session agreed on <see cref="ServerCrypto" />'s key pair, ready once it has the peer's key.</summary>
    public IAvalonCryptoSession Session { get; }
}
