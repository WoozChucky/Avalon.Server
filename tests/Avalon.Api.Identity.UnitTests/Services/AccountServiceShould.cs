using Avalon.Api.Hosting.Exceptions;
using Avalon.Api.Identity.Config;
using Avalon.Api.Identity.Services;
using Avalon.Api.Testing;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.Services;

public class AccountServiceShould
{
    private readonly IAccountRepository _accountRepository = Substitute.For<IAccountRepository>();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly Avalon.Infrastructure.GameAuth.IGameContextRevocations _revocations =
        Substitute.For<Avalon.Infrastructure.GameAuth.IGameContextRevocations>();

    private readonly IDbTransactionRunner<AuthDbContext> _transaction =
        Substitute.For<IDbTransactionRunner<AuthDbContext>>();

    private AccountService CreateService() => new(
        NullLoggerFactory.Instance,
        _accountRepository,
        Substitute.For<Avalon.Api.Identity.Authentication.Jwt.IJwtUtils>(),
        Substitute.For<IMFAHashService>(),
        Substitute.For<IMfaSetupRepository>(),
        Substitute.For<IDeviceRepository>(),
        _cache,
        Substitute.For<ISecureRandom>(),
        _transaction,
        new AuthenticationConfig(),
        TestLogin.Password(_accountRepository, _cache),
        TestLogin.Reauthentication(_accountRepository, _cache),
        contextRevocations: _revocations);

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
    }

    /// <summary>
    /// #478 review: the email-change token was read, then deleted, so two confirms sent together
    /// could both read it. Only the confirm whose delete removed it goes on.
    /// </summary>
    [Fact]
    public async Task Refuse_an_email_change_token_another_confirm_spent_first()
    {
        _cache.GetAsync(AccountService.EmailChangeKey("token")).Returns("7|0|new@avalon.monster");
        _cache.RemoveAsync(AccountService.EmailChangeKey("token")).Returns(false);

        BusinessException refused = await Assert.ThrowsAsync<Avalon.Api.Hosting.Exceptions.BusinessException>(
            () => CreateService().ConfirmEmailChangeAsync("token"));

        Assert.Equal("Invalid or expired token", refused.Message);
        await _transaction.DidNotReceiveWithAnyArgs().ExecuteAsync(default(Func<AuthDbContext, CancellationToken, Task<bool>>)!, default);
    }

    /// <summary>#478 review: the ban is committed, so a failed disconnect publish must not fail the call.</summary>
    [Fact]
    public async Task Ban_even_when_the_disconnect_publish_fails()
    {
        _cache.PublishAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task>(_ => throw new StackExchange.Redis.RedisConnectionException(
                StackExchange.Redis.ConnectionFailureType.UnableToConnect, StackExchange.Redis.CommandFlags.CommandRetryNever, "down"));

        await CreateService().UpdateStatusAsync(new AccountId(7), Contract.AccountStatus.Banned, "spam", new AccountId(1));
    }

    /// <summary>
    /// #882: a ban is published with its status, instead of the bare disconnect, so the servers can tell the player
    /// why; and every world holding a session of the account is asked for a heartbeat at once.
    /// </summary>
    [Theory]
    [InlineData(Contract.AccountStatus.Banned, "7|BANNED")]
    [InlineData(Contract.AccountStatus.Deactivated, "7|DEACTIVATED")]
    public async Task Tell_the_servers_why_an_account_was_dropped(Contract.AccountStatus status, string notice)
    {
        AccountService service = CreateService();

        await service.UpdateStatusAsync(new AccountId(7), status, "spam", new AccountId(1));

        await _cache.Received(1).PublishAsync(CacheKeys.WorldAccountsStatusChannel, notice);
        await _cache.DidNotReceive().PublishAsync(CacheKeys.WorldAccountsDisconnectChannel, Arg.Any<string>());
        await _revocations.Received(1).PublishAsync(new AccountId(7), Guid.Empty);
    }
}
