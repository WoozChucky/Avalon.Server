using Avalon.Common.Cryptography;
using Avalon.Infrastructure.GameAuth;
using Avalon.Server.World.Handlers;
using Avalon.World.GameAuth;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.GameAuth;

public sealed class GameAdmissionHandlerShould
{
    [Fact]
    public async Task Publish_only_active_trusted_identity_on_a_tick_continuation()
    {
        using var connection = WorldAdmissionConnection.Create();
        IGameAdmissionClient api = Substitute.For<IGameAdmissionClient>(); GameSessionLease lease = WorldAdmissionConnection.Lease();
        api.AdmitAsync(Arg.Any<string>(), connection.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new WorldAdmissionResult(lease, null));
        await new GameAdmissionHandler(api).ExecuteAsync(new()
        {
            Connection = connection,
            Packet = new() { JoinTicket = GameAuthCryptography.NewToken(), PublicKey = new CryptoManager().GetPublicKey() }
        });
        Assert.Null(connection.AccountId); Assert.Null(connection.GameplayAuthority);
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (connection.AccountId is null && DateTime.UtcNow < deadline) { connection.FlushContinuations(); await Task.Yield(); }
        Assert.Equal(lease.Authority.AccountId, connection.AccountId);
        Assert.Same(lease.Authority, connection.GameplayAuthority);
        Assert.False(connection.IsGameplayAuthorized); // The supported-version handshake still must finish.
        Assert.Single(connection.Sent);
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
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Reject_plaintext_or_malformed_key_before_backend_redemption(bool tls, bool malformedKey)
    {
        using var connection = WorldAdmissionConnection.Create(tls: tls); IGameAdmissionClient api = Substitute.For<IGameAdmissionClient>();
        await new GameAdmissionHandler(api).ExecuteAsync(new()
        {
            Connection = connection,
            Packet = new() { JoinTicket = GameAuthCryptography.NewToken(), PublicKey = malformedKey ? [] : new CryptoManager().GetPublicKey() }
        });
        await api.DidNotReceiveWithAnyArgs().AdmitAsync(default!, default, default, default);
        Assert.True(connection.IsClosing); Assert.Null(connection.GameplayAuthority);
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
