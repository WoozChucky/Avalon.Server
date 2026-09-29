using Avalon.Common.Accounts;
using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.GameTickets;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth;
using Avalon.Server.Auth.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class CAuthGameTicketHandlerShould
{
    private readonly IGameTicketStore _tickets = Substitute.For<IGameTicketStore>();
    private readonly IRefreshTokenRepository _families = Substitute.For<IRefreshTokenRepository>();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly Guid _family = Guid.NewGuid();
    private readonly Account _account = new()
    {
        Id = new AccountId(12), Username = "PLAYER", Email = "p@example.com",
        Salt = [], Verifier = [], JoinDate = DateTime.UtcNow,
        AccessLevel = AccountAccessLevel.Player, CredentialsVersion = 4
    };

    public CAuthGameTicketHandlerShould()
    {
        _connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        _connection.RemoteEndPoint.Returns("127.0.0.1:1234");
        _connection.Id.Returns(Guid.NewGuid());
        _tickets.RedeemAsync("ticket", Arg.Any<CancellationToken>())
            .Returns(new GameTicketGrant(_account.Id, _family, 4), (GameTicketGrant?)null);
        _families.IsLiveLauncherFamilyAsync(_account.Id, _family, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _accounts.FindByIdAsync(_account.Id, false, Arg.Any<CancellationToken>()).Returns(_account);
        _accounts.TryRecordLoginAsync(default!, default!, default, default, default).ReturnsForAnyArgs(true);
    }

    private CAuthGameTicketHandler Handler() => new(NullLoggerFactory.Instance, _tickets, _families, _accounts, _cache);

    private async Task<AuthResult> SendAsync(string ticket = "ticket")
    {
        await Handler().ExecuteAsync(new AuthPacketContext<CAuthGameTicketPacket>
            { Packet = new CAuthGameTicketPacket { Ticket = ticket }, Connection = _connection });
        var sent = _connection.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IAuthConnection.Send))
            .Select(c => c.GetArguments()[0]).OfType<NetworkPacket>().Last();
        return Serializer.Deserialize<SAuthResultPacket>(new MemoryStream(sent.Payload)).Result;
    }

    [Fact]
    public async Task Redeem_once_and_complete_the_game_session()
    {
        Assert.Equal(AuthResult.SUCCESS, await SendAsync());
        Assert.Equal(_account.Id, _connection.AccountId);
        Assert.Equal(4, _connection.CredentialsVersion);
        await _accounts.Received(1).TryRecordLoginAsync(_account.Id, "127.0.0.1", Arg.Any<DateTime>(),
            _connection.Id, Arg.Any<CancellationToken>());
        Assert.Equal(AuthResult.INVALID_CREDENTIALS, await SendAsync());
        await _accounts.Received(1).TryRecordLoginAsync(Arg.Any<AccountId>(), Arg.Any<string>(),
            Arg.Any<DateTime>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuse_when_launcher_family_has_been_revoked()
    {
        _families.IsLiveLauncherFamilyAsync(_account.Id, _family, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);
        Assert.Equal(AuthResult.INVALID_CREDENTIALS, await SendAsync());
        await _accounts.DidNotReceiveWithAnyArgs().TryRecordLoginAsync(default!, default!, default, default, default);
        await _cache.DidNotReceiveWithAnyArgs().IncrementAsync(default!, default);
    }

    [Fact]
    public async Task Refuse_when_credentials_version_has_changed()
    {
        _account.CredentialsVersion++;
        Assert.Equal(AuthResult.INVALID_CREDENTIALS, await SendAsync());
        await _cache.DidNotReceiveWithAnyArgs().IncrementAsync(default!, default);
    }

    [Fact]
    public async Task Refuse_when_guarded_login_recording_loses_to_a_lock()
    {
        _accounts.TryRecordLoginAsync(default!, default!, default, default, default).ReturnsForAnyArgs(false);
        Assert.Equal(AuthResult.LOCKED, await SendAsync());
        Assert.Null(_connection.AccountId);
    }

    [Fact]
    public async Task Refuse_locked_or_inactive_or_non_player_accounts()
    {
        _tickets.RedeemAsync("ticket", Arg.Any<CancellationToken>())
            .Returns(_ => new GameTicketGrant(_account.Id, _family, 4));
        _account.Locked = true;
        Assert.Equal(AuthResult.LOCKED, await SendAsync());
        _account.Locked = false;
        _account.Status = AccountStatus.Banned;
        Assert.Equal(AuthResult.BANNED, await SendAsync());
        _account.Status = AccountStatus.Active;
        _account.AccessLevel = AccountAccessLevel.Console;
        Assert.Equal(AuthResult.INVALID_CREDENTIALS, await SendAsync());
    }
}
