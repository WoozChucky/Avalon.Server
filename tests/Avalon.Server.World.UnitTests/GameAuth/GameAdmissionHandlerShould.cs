using Avalon.Common.Cryptography;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure.GameAuth;
using Avalon.Network.Packets.Auth;
using Avalon.Server.World.Handlers;
using Avalon.World;
using Avalon.World.GameAuth;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.GameAuth;

public sealed class GameAdmissionHandlerShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_only_active_trusted_identity_on_a_tick_continuation(bool packetEncryption)
    {
        IWorldServer server = Substitute.For<IWorldServer, IServerBase>();
        server.PacketEncryption.Returns(packetEncryption);
        using var connection = WorldAdmissionConnection.Create(server);
        IGameAdmissionClient api = Substitute.For<IGameAdmissionClient>(); GameSessionLease lease = WorldAdmissionConnection.Lease();
        api.AdmitAsync(Arg.Any<string>(), connection.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new WorldAdmissionResult(lease, null));
        // A 0.3.0 client sends a public key of its own in both modes: it learns the mode from the reply.
        byte[] clientKey = new CryptoManager().GetPublicKey();
        await new GameAdmissionHandler(api).ExecuteAsync(new()
        {
            Connection = connection,
            Packet = new() { JoinTicket = GameAuthCryptography.NewToken(), PublicKey = clientKey }
        });
        Assert.Null(connection.AccountId); Assert.Null(connection.GameplayAuthority);
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (connection.AccountId is null && DateTime.UtcNow < deadline) { connection.FlushContinuations(); await Task.Yield(); }
        Assert.Equal(lease.Authority.AccountId, connection.AccountId);
        Assert.Same(lease.Authority, connection.GameplayAuthority);
        Assert.False(connection.IsGameplayAuthorized); // The supported-version handshake still must finish.

        // The reply tells the client how to send (#875): sealed exactly when this world seals.
        SGameAdmissionPacket reply = Serializer.Deserialize<SGameAdmissionPacket>(Assert.Single(connection.Sent).PayloadMemory);
        Assert.Equal(GameAdmissionResult.Accepted, reply.Result);
        Assert.Equal(packetEncryption, reply.PacketEncryption);

        // The key exchange runs only where the world seals (#878): there the session is keyed from the client's key and
        // the reply carries the connection's own; a plain world keys nothing and sends no key.
        if (packetEncryption)
        {
            Assert.Equal(clientKey, connection.CryptoSession!.GetOtherEndPublicKey());
            Assert.Equal(connection.ServerCrypto!.GetPublicKey(), reply.PublicKey);
        }
        else
        {
            Assert.Null(connection.CryptoSession);
            Assert.Empty(reply.PublicKey);
        }
    }
    [Fact]
    public async Task Reject_a_second_admission_before_spending_its_ticket()
    {
        using var connection = WorldAdmissionConnection.Create(); Assert.True(connection.TryBeginAdmission());
        IGameAdmissionClient api = Substitute.For<IGameAdmissionClient>();
        await new GameAdmissionHandler(api).ExecuteAsync(new()
        {
            Connection = connection,
            Packet = new() { JoinTicket = GameAuthCryptography.NewToken(), PublicKey = new CryptoManager().GetPublicKey() }
        });
        await api.DidNotReceiveWithAnyArgs().AdmitAsync(default!, default, default, default);
        Assert.True(connection.IsClosing); Assert.Null(connection.AccountId);
    }
    public enum ClientKey { Valid, Empty, OffTheCurve }

    /// <summary>
    /// A connection without TLS, or a key of the wrong length on a world that seals, is refused before the ticket is
    /// spent. A key of the right length that is not a P-256 point is found when the session is keyed, after the
    /// redemption: the client is told <c>InvalidRequest</c>, the lease is revoked, and the close is counted as the peer's,
    /// <c>bad_public_key</c>, not taken for a server fault. A world that does not seal runs no key exchange (#878), so
    /// whatever key the client sends there is not read.
    /// </summary>
    [Theory]
    [InlineData(false, ClientKey.Valid, false, false, null)]
    [InlineData(true, ClientKey.Empty, true, false, null)]
    [InlineData(true, ClientKey.Empty, false, true, null)]
    [InlineData(true, ClientKey.OffTheCurve, true, true, "bad_public_key")]
    public async Task Refuse_a_connection_without_tls_or_a_client_key_that_is_not_one(bool tls, ClientKey key,
        bool packetEncryption, bool redeemed, string? violation)
    {
        IWorldServer server = Substitute.For<IWorldServer, IServerBase>();
        server.PacketEncryption.Returns(packetEncryption);
        using var connection = WorldAdmissionConnection.Create(server, tls: tls); IGameAdmissionClient api = Substitute.For<IGameAdmissionClient>();
        GameSessionLease lease = WorldAdmissionConnection.Lease();
        api.AdmitAsync(Arg.Any<string>(), connection.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new WorldAdmissionResult(lease, null));
        byte[] publicKey = new CryptoManager().GetPublicKey();
        if (key == ClientKey.OffTheCurve) publicKey[^1] ^= 0x01;   // the point's y moved: the right length, no longer on P-256
        await new GameAdmissionHandler(api).ExecuteAsync(new()
        {
            Connection = connection,
            Packet = new() { JoinTicket = GameAuthCryptography.NewToken(), PublicKey = key == ClientKey.Empty ? [] : publicKey }
        });
        // The ticket is redeemed, and the session keyed, off the calling thread; the outcome is applied on the tick.
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (redeemed && connection.AccountId is null && !connection.IsClosing && DateTime.UtcNow < deadline)
        {
            connection.FlushContinuations();
            await Task.Yield();
        }
        await api.Received(redeemed ? 1 : 0).AdmitAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        bool admitted = redeemed && violation is null;
        Assert.Equal(!admitted, connection.IsClosing);
        Assert.Equal(admitted, connection.GameplayAuthority is not null);
        Assert.Equal(violation is null ? [] : [violation], connection.Violations);
        if (violation is not null)
        {
            Assert.Equal(GameAdmissionResult.InvalidRequest,
                Serializer.Deserialize<SGameAdmissionPacket>(Assert.Single(connection.Sent).PayloadMemory).Result);
            Assert.False(lease.IsActive);
        }
    }
    [Fact]
    public async Task Never_bind_or_load_a_character_when_backend_redemption_fails()
    {
        using var connection = WorldAdmissionConnection.Create(); IGameAdmissionClient api = Substitute.For<IGameAdmissionClient>();
        api.AdmitAsync(Arg.Any<string>(), connection.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new WorldAdmissionResult(null, "INVALID_TICKET"));
        await new GameAdmissionHandler(api).ExecuteAsync(new()
        {
            Connection = connection,
            Packet = new() { JoinTicket = GameAuthCryptography.NewToken(), PublicKey = new CryptoManager().GetPublicKey() }
        });
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!connection.IsClosing && DateTime.UtcNow < deadline) { connection.FlushContinuations(); await Task.Yield(); }
        Assert.True(connection.IsClosing); Assert.Null(connection.AccountId); Assert.Null(connection.GameplayAuthority); Assert.Null(connection.Character);
    }
}
