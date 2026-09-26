using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth.Configuration;
using Avalon.Server.Auth.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using AvalonWorld = Avalon.Domain.Auth.World;

namespace Avalon.Server.Auth.UnitTests.Handlers;

/// <summary>
/// #495 review: the post-login handlers checked only that the connection had logged in, so a
/// session whose account was banned, or whose credentials changed, since its login could still set
/// up, confirm or reset MFA. Every one of them now runs <see cref="PostLoginGuard"/>: a fresh read,
/// Active, and the connection's credentials version, or the connection is closed and nothing done.
/// </summary>
public sealed class PostLoginGuardShould
{
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IMFAService _mfa = Substitute.For<IMFAService>();
    private readonly IWorldRepository _worlds = Substitute.For<IWorldRepository>();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();

    public PostLoginGuardShould()
    {
        _connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        _connection.AccountId.Returns(new AccountId(1L));
        _connection.CredentialsVersion.Returns(3);
        _worlds.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<AvalonWorld> { World() });
        _worlds.FindByIdAsync(Arg.Any<WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(World());
        _cache.SetNxAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>()).Returns(true);
        ISecureRandom random = Substitute.For<ISecureRandom>();
        random.GetBytes(32).Returns(new byte[32]);
        _random = random;
    }

    private readonly ISecureRandom _random;

    private static AvalonWorld World() => new()
    {
        Id = new WorldId(1), Name = "World", Host = "localhost", Port = 7001, MinVersion = "0.0.1",
        Version = "0.0.1", AccessLevelRequired = AccountAccessLevel.Player,
    };

    private void AccountIs(AccountStatus status, int credentialsVersion) =>
        _accounts.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new Account
        {
            Id = new AccountId(1L), Username = "TESTUSER", Email = "t@t", Salt = [1], Verifier = [2],
            JoinDate = DateTime.UtcNow, AccessLevel = AccountAccessLevel.Player, Status = status,
            CredentialsVersion = credentialsVersion,
        });

    public static TheoryData<string> Handlers => ["MFA setup", "MFA confirm", "MFA reset", "world list", "world select"];

    private Task RunAsync(string handler) => handler switch
    {
        "MFA setup" => new CMFASetupHandler(NullLoggerFactory.Instance, _mfa, _accounts,
                Options.Create(new AuthConfiguration { Issuer = "Avalon" }))
            .ExecuteAsync(new AuthPacketContext<CMFASetupPacket> { Packet = new CMFASetupPacket(), Connection = _connection }),
        "MFA confirm" => new CMFAConfirmHandler(NullLoggerFactory.Instance, _mfa, _accounts)
            .ExecuteAsync(new AuthPacketContext<CMFAConfirmPacket>
                { Packet = new CMFAConfirmPacket { Code = "123456" }, Connection = _connection }),
        "MFA reset" => new CMFAResetHandler(NullLoggerFactory.Instance, _mfa, _accounts)
            .ExecuteAsync(new AuthPacketContext<CMFAResetPacket>
            {
                Packet = new CMFAResetPacket { RecoveryCode1 = "a", RecoveryCode2 = "b", RecoveryCode3 = "c" },
                Connection = _connection,
            }),
        "world list" => new CWorldListHandler(NullLoggerFactory.Instance, _worlds, _accounts)
            .ExecuteAsync(new AuthPacketContext<CWorldListPacket> { Packet = new CWorldListPacket(), Connection = _connection }),
        "world select" => new CWorldSelectHandler(NullLoggerFactory.Instance, _cache, _accounts, _worlds, _random)
            .ExecuteAsync(new AuthPacketContext<CWorldSelectPacket>
                { Packet = new CWorldSelectPacket { WorldId = new WorldId(1) }, Connection = _connection }),
        _ => throw new ArgumentOutOfRangeException(nameof(handler)),
    };

    private async Task AssertRefusedAsync()
    {
        _connection.Received(1).Close();
        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        Assert.Empty(_mfa.ReceivedCalls());
        await _cache.DidNotReceiveWithAnyArgs().SetAsync(default!, default!, default);
    }

    [Theory]
    [MemberData(nameof(Handlers))]
    public async Task Close_a_session_whose_account_was_banned_since_its_login(string handler)
    {
        AccountIs(AccountStatus.Banned, credentialsVersion: 3);

        await RunAsync(handler);

        await AssertRefusedAsync();
    }

    [Theory]
    [MemberData(nameof(Handlers))]
    public async Task Close_a_session_whose_account_was_deactivated_since_its_login(string handler)
    {
        AccountIs(AccountStatus.Deactivated, credentialsVersion: 3);

        await RunAsync(handler);

        await AssertRefusedAsync();
    }

    [Theory]
    [MemberData(nameof(Handlers))]
    public async Task Close_a_session_whose_credentials_changed_since_its_login(string handler)
    {
        AccountIs(AccountStatus.Active, credentialsVersion: 4);

        await RunAsync(handler);

        await AssertRefusedAsync();
    }

    [Theory]
    [MemberData(nameof(Handlers))]
    public async Task Serve_a_session_that_is_still_current(string handler)
    {
        AccountIs(AccountStatus.Active, credentialsVersion: 3);
        _mfa.SetupMFAAsync(default!, default!, default).ReturnsForAnyArgs(new MFASetupResult(true, "otpauth://x", MFAOperationResult.Success));
        _mfa.ConfirmMFAAsync(default!, default!, default).ReturnsForAnyArgs(new MFAConfirmResult(true, ["a", "b", "c"], MFAOperationResult.Success));
        _mfa.ResetMFAAsync(default!, default!, default!, default!, default).ReturnsForAnyArgs(new MFAResetResult(true, MFAOperationResult.Success));

        await RunAsync(handler);

        _connection.DidNotReceive().Close();
        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }
}
