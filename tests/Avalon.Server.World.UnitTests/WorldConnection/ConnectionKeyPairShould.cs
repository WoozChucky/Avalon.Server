// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.World;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.WorldConnection;

/// <summary>
/// Each connection agrees its session key on a key pair of its own.
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

    private IConnection NewConnection(IWorldServer server)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var clientSide = new TcpClient();
        clientSide.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint!).Port);
        TcpClient serverSide = listener.AcceptTcpClient();
        listener.Stop();

        _sockets.Add(clientSide);
        _sockets.Add(serverSide);

        return new Avalon.World.WorldConnection(
            server, clientSide, NullLoggerFactory.Instance, Substitute.For<IPacketReader>());
    }

    [Fact]
    public void DifferFromEveryOtherConnectionOnTheSameServer()
    {
        var server = Substitute.For<IWorldServer, IServerBase>();
        ((IServerBase)server).SendBufferCapacity.Returns(256);

        IConnection first = NewConnection(server);
        IConnection second = NewConnection(server);

        Assert.NotEqual(first.ServerCrypto.GetPublicKey(), second.ServerCrypto.GetPublicKey());

        // And the session really agrees on the connection's own pair, rather than on one key while
        // the handshake announces another. The session only knows its own public key once it has a
        // peer's, so both are initialized first.
        var peer = new Avalon.Common.Cryptography.CryptoManager();
        first.CryptoSession.Initialize(peer.GetPublicKey());
        second.CryptoSession.Initialize(peer.GetPublicKey());

        Assert.Equal(first.ServerCrypto.GetPublicKey(), first.CryptoSession.GetPublicKey());
        Assert.Equal(second.ServerCrypto.GetPublicKey(), second.CryptoSession.GetPublicKey());
    }

    /// <summary>
    /// The one the client cannot be trusted with: it reuses its key pair, and the two sessions
    /// still seal their first packets under different keys.
    /// </summary>
    [Fact]
    public void SealDifferentlyOnTwoConnectionsFromOnePeerKeyPair()
    {
        var server = Substitute.For<IWorldServer, IServerBase>();
        ((IServerBase)server).SendBufferCapacity.Returns(256);

        // One client key pair, presented twice.
        var peer = new Avalon.Common.Cryptography.CryptoManager();

        IConnection first = NewConnection(server);
        IConnection second = NewConnection(server);

        first.CryptoSession.Initialize(peer.GetPublicKey());
        second.CryptoSession.Initialize(peer.GetPublicKey());

        byte[] a = first.CryptoSession.Encrypt("the same plaintext"u8);
        byte[] b = second.CryptoSession.Encrypt("the same plaintext"u8);

        // Both nonces are zero — that is what a counter does, and it is not the problem. The
        // ciphertexts differing is what says the keys are not shared.
        Assert.Equal(a[..12], b[..12]);
        Assert.NotEqual(a[12..], b[12..]);
    }
}
