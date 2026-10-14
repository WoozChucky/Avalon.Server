using System.Net;
using System.Net.Sockets;
using Avalon.Common.Cryptography;
using Avalon.Hosting.Networking;
using Avalon.World;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.WorldConnection;

/// <summary>
/// Each connection of a world that seals (<c>Network:PacketEncryption</c>) agrees its session key on a key pair of its
/// own. A world that does not seal runs no key exchange (#878), so its connections have none to share.
/// </summary>
/// <remarks>
/// <para>
/// The session nonce is a counter from zero, so a counter value repeats on every connection. That
/// is safe only while no two sessions hold the same key — two that did would seal their first
/// packets under one key and one nonce, which recovers the GCM authentication subkey for a passive
/// listener, and neither end would see anything wrong.
/// </para>
/// <para>
/// While the key pair was the server's rather than the connection's, reaching that took nothing
/// but a client reusing its own key pair across two connections: same peer key, same server key,
/// same salt, same secret, same derived key, both counters at zero. It was the far end's choice
/// and the near end could neither detect it nor prevent it, which is not a property to rest on.
/// </para>
/// </remarks>
public class ConnectionKeyPairShould : IDisposable
{
    private readonly List<TcpClient> _sockets = [];

    public void Dispose()
    {
        foreach (TcpClient socket in _sockets) socket.Dispose();
        GC.SuppressFinalize(this);
    }

    private (ICryptoManager Keys, IAvalonCryptoSession Session) NewConnection(IWorldServer server)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var clientSide = new TcpClient();
        clientSide.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint!).Port);
        TcpClient serverSide = listener.AcceptTcpClient();
        listener.Stop();

        _sockets.Add(clientSide);
        _sockets.Add(serverSide);

        var connection = new Avalon.World.WorldConnection(
            server, clientSide, NullLoggerFactory.Instance, Substitute.For<IPacketReader>());
        return (connection.ServerCrypto!, connection.CryptoSession!);
    }

    private static IWorldServer SealingServer()
    {
        IWorldServer server = Substitute.For<IWorldServer, IServerBase>();
        server.PacketEncryption.Returns(true);
        return server;
    }

    [Fact]
    public void DifferFromEveryOtherConnectionOnTheSameServer()
    {
        IWorldServer server = SealingServer();

        (ICryptoManager Keys, IAvalonCryptoSession Session) first = NewConnection(server);
        (ICryptoManager Keys, IAvalonCryptoSession Session) second = NewConnection(server);

        Assert.NotEqual(first.Keys.GetPublicKey(), second.Keys.GetPublicKey());

        // And the session really agrees on the connection's own pair, rather than on one key while
        // the handshake announces another. The session only knows its own public key once it has a
        // peer's, so both are initialized first.
        var peer = new CryptoManager();
        first.Session.Initialize(peer.GetPublicKey());
        second.Session.Initialize(peer.GetPublicKey());

        Assert.Equal(first.Keys.GetPublicKey(), first.Session.GetPublicKey());
        Assert.Equal(second.Keys.GetPublicKey(), second.Session.GetPublicKey());
    }

    /// <summary>
    /// The one the client cannot be trusted with: it reuses its key pair, and the two sessions
    /// still seal their first packets under different keys.
    /// </summary>
    [Fact]
    public void SealDifferentlyOnTwoConnectionsFromOnePeerKeyPair()
    {
        IWorldServer server = SealingServer();

        // One client key pair, presented twice.
        var peer = new CryptoManager();

        (ICryptoManager Keys, IAvalonCryptoSession Session) first = NewConnection(server);
        (ICryptoManager Keys, IAvalonCryptoSession Session) second = NewConnection(server);

        first.Session.Initialize(peer.GetPublicKey());
        second.Session.Initialize(peer.GetPublicKey());

        byte[] a = first.Session.Encrypt("the same plaintext"u8);
        byte[] b = second.Session.Encrypt("the same plaintext"u8);

        // Both nonces are zero — that is what a counter does, and it is not the problem. The
        // ciphertexts differing is what says the keys are not shared.
        Assert.Equal(a[..12], b[..12]);
        Assert.NotEqual(a[12..], b[12..]);
    }
}
