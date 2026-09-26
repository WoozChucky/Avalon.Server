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

    /// <summary>
    /// #478 review: the email-change token was read, then deleted, so two confirms sent together
    /// could both read it. Only the confirm whose delete removed it goes on.
    /// </summary>
    [Fact]
    public async Task Refuse_an_email_change_token_another_confirm_spent_first()
    {
        _cache.GetAsync("auth:emailChange:token").Returns("7|0|new@avalon.monster");
        _cache.RemoveAsync("auth:emailChange:token").Returns(false);

        var refused = await Assert.ThrowsAsync<Avalon.Api.Exceptions.BusinessException>(
            () => CreateService().ConfirmEmailChangeAsync("token"));

        Assert.Equal("Invalid or expired token", refused.Message);
        await _transaction.DidNotReceiveWithAnyArgs().ExecuteAsync(default(Func<AuthDbContext, CancellationToken, Task<bool>>)!, default);
    }

    [Fact]
    public async Task Confirm_an_email_change_with_the_token_it_spent()
    {
        _cache.GetAsync("auth:emailChange:token").Returns("7|0|new@avalon.monster");
        _cache.RemoveAsync("auth:emailChange:token").Returns(true);
        _transaction.ExecuteAsync(Arg.Any<Func<AuthDbContext, CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await CreateService().ConfirmEmailChangeAsync("token");

        await _transaction.Received(1).ExecuteAsync(Arg.Any<Func<AuthDbContext, CancellationToken, Task<bool>>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>#478 review: the ban is committed, so a failed disconnect publish must not fail the call.</summary>
    [Fact]
    public async Task Ban_even_when_the_disconnect_publish_fails()
    {
        _cache.PublishAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task>(_ => throw new StackExchange.Redis.RedisConnectionException(
                StackExchange.Redis.ConnectionFailureType.UnableToConnect, "down"));

        await CreateService().UpdateStatusAsync(new AccountId(7), Contract.AccountStatus.Banned, "spam", new AccountId(1));
    }

    [Fact]
    public async Task Tell_the_world_server_to_drop_a_banned_account()
    {
        AccountService service = CreateService();

        await service.UpdateStatusAsync(new AccountId(7), Contract.AccountStatus.Banned, "spam", new AccountId(1));

        await _cache.Received(1).PublishAsync(CacheKeys.WorldAccountsDisconnectChannel, "7");
    }
}
