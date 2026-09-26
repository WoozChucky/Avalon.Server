using System.Net;
using System.Security.Authentication;
using System.Text;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Api.Exceptions;
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
using Account = Avalon.Domain.Auth.Account;
using DomainStatus = Avalon.Domain.Auth.AccountStatus;
using MFASetup = Avalon.Domain.Auth.MFASetup;
using MfaSetupStatus = Avalon.Domain.Auth.MfaSetupStatus;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// #480: password login must hand nothing to an account that is not Active — no access token, no
/// MFA hash to finish the login with. Once the password is right the caller is told the account's
/// status, as the game client is; with a wrong password the answer is the generic one whatever
/// the status, so without the password nothing about the account can be learned.
/// </summary>
public class AccountLoginStatusShould
{
    private static readonly string Password = TestPasswords.Valid;

    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IJwtUtils _jwt = Substitute.For<IJwtUtils>();
    private readonly IMFAHashService _mfaHash = Substitute.For<IMFAHashService>();
    private readonly IMfaSetupRepository _mfaSetups = Substitute.For<IMfaSetupRepository>();

    private AccountService CreateService() => new(
        NullLoggerFactory.Instance,
        _accounts,
        _jwt,
        _mfaHash,
        _mfaSetups,
        Substitute.For<IDeviceRepository>(),
        Substitute.For<IReplicatedCache>(),
        Substitute.For<ISecureRandom>(),
        Substitute.For<IDbTransactionRunner<AuthDbContext>>(),
        new AuthenticationConfig(),
        TestLogin.Password(_accounts, Substitute.For<IReplicatedCache>()),
        TestLogin.Reauthentication(_accounts, Substitute.For<IReplicatedCache>()));

    private static readonly byte[] Verifier =
        Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword(Password, BCrypt.Net.BCrypt.GenerateSalt(4)));

    private void AccountIs(DomainStatus status, bool mfa = false)
    {
        var account = new Account
        {
            Id = new AccountId(7), Username = "CALLER", Email = "c@avalon.monster",
            Salt = [1], Verifier = Verifier, JoinDate = DateTime.UtcNow, Status = status,
        };
        _accounts.FindByUserNameAsync("CALLER", Arg.Any<CancellationToken>()).Returns(account);
        _mfaSetups.FindByAccountIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns(mfa ? new MFASetup { Account = account, AccountId = account.Id, Secret = [1], Status = MfaSetupStatus.Confirmed } : null);
        _jwt.GenerateJwtToken(Arg.Any<Account>()).Returns("jwt");
        _accounts.TryRecordApiLoginAsync(account.Id, Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private Task<(AuthenticateResponse Response, AccountId? AccountId, int CredentialsVersion)> LoginAsync(string password) =>
        CreateService().Authenticate(new AuthenticateRequest { Username = "caller", Password = password },
            IPAddress.Loopback, CancellationToken.None);

    [Fact]
    public async Task Log_in_an_active_account()
    {
        AccountIs(DomainStatus.Active);

        var (response, accountId, _) = await LoginAsync(Password);

        Assert.Equal("jwt", response.Token);
        Assert.Equal(7, accountId!.Value);
    }

    [Theory]
    [InlineData(DomainStatus.Banned, false, "BANNED")]
    [InlineData(DomainStatus.Deactivated, false, "DEACTIVATED")]
    [InlineData(DomainStatus.Banned, true, "BANNED")]
    [InlineData(DomainStatus.Deactivated, true, "DEACTIVATED")]
    public async Task Tell_an_account_that_is_not_active_its_status_once_the_password_is_right(
        DomainStatus status, bool mfa, string expected)
    {
        AccountIs(status, mfa);

        var refused = await Assert.ThrowsAsync<AccountInactiveException>(() => LoginAsync(Password));

        Assert.Equal(status, refused.Status);
        Assert.Equal(expected, refused.Message);
        _jwt.DidNotReceiveWithAnyArgs().GenerateJwtToken(default!);
        await _mfaHash.DidNotReceiveWithAnyArgs().GenerateHashAsync(default!);
        await _accounts.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    // Without the password nothing about the account's status is given away.
    [Theory]
    [InlineData(DomainStatus.Active)]
    [InlineData(DomainStatus.Banned)]
    [InlineData(DomainStatus.Deactivated)]
    public async Task Give_a_wrong_password_the_generic_answer_whatever_the_status(DomainStatus status)
    {
        AccountIs(status);

        var refused = await Assert.ThrowsAsync<AuthenticationException>(() => LoginAsync(TestPasswords.Wrong));

        Assert.Equal("Invalid username or password", refused.Message);
        _jwt.DidNotReceiveWithAnyArgs().GenerateJwtToken(default!);
    }
}
