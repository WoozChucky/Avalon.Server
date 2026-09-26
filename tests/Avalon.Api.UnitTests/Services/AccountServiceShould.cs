using Avalon.Api.Config;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

public class AccountServiceShould
{
    private readonly IAccountRepository _accountRepository = Substitute.For<IAccountRepository>();
    private readonly IRefreshTokenService _refreshService = Substitute.For<IRefreshTokenService>();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();

    private readonly IDbTransactionRunner<AuthDbContext> _transaction =
        Substitute.For<IDbTransactionRunner<AuthDbContext>>();

    private AccountService CreateService() => new(
        NullLoggerFactory.Instance,
        _accountRepository,
        Substitute.For<Avalon.Api.Authentication.Jwt.IJwtUtils>(),
        Substitute.For<IMFAHashService>(),
        Substitute.For<IMfaSetupRepository>(),
        Substitute.For<IDeviceRepository>(),
        _cache,
        Substitute.For<ISecureRandom>(),
        _refreshService,
        _transaction,
        new AuthenticationConfig(),
        TestLogin.Password(_accountRepository, _cache),
        TestLogin.Reauthentication(_accountRepository, _cache));

    /// <summary>
    /// The substituted runner never invokes the body, so anything a collaborator still receives is
    /// a write that was hoisted out of the transaction — the shape this had before, when the
    /// repositories shared the service's context and a repository call did join the transaction.
    /// AccountStatusChangeShould covers the body itself against a real database.
    /// </summary>
    [Fact]
    public async Task Write_no_part_of_a_status_change_outside_the_transaction_it_opens()
    {
        AccountService service = CreateService();

        await service.UpdateStatusAsync(new AccountId(7), Contract.AccountStatus.Banned, "spam", new AccountId(1));

        await _transaction.Received(1).ExecuteAsync(Arg.Any<Func<AuthDbContext, CancellationToken, Task>>(),
            Arg.Any<CancellationToken>());
        await _accountRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        await _refreshService.DidNotReceiveWithAnyArgs().RevokeAllForAccountAsync(default, default);
    }

    [Fact]
    public async Task Tell_the_world_server_to_drop_a_banned_account()
    {
        AccountService service = CreateService();

        await service.UpdateStatusAsync(new AccountId(7), Contract.AccountStatus.Banned, "spam", new AccountId(1));

        await _cache.Received(1).PublishAsync(CacheKeys.WorldAccountsDisconnectChannel, "7");
    }
}
