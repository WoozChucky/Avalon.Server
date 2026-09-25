using System.Net;
using System.Security.Authentication;
using System.Text;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Contract;
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
/// MFA hash to finish the login with — and must answer exactly as it does for a wrong password,
/// so the endpoint does not reveal which accounts are banned. The check runs after the password
/// verify, so without the password nothing about the account's status can be learned at all.
/// </summary>
public class AccountLoginStatusShould
{
    private const string Password = "correct horse";

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
        Substitute.For<IRefreshTokenService>(),
        Substitute.For<IDbTransactionRunner<AuthDbContext>>(),
        new AuthenticationConfig());

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
    }

    private Task<(AuthenticateResponse Response, AccountId? AccountId)> LoginAsync(string password) =>
        CreateService().Authenticate(new AuthenticateRequest { Username = "caller", Password = password },
            IPAddress.Loopback, CancellationToken.None);

    [Fact]
    public async Task Log_in_an_active_account()
    {
        AccountIs(DomainStatus.Active);

        var (response, accountId) = await LoginAsync(Password);

        Assert.Equal("jwt", response.Token);
        Assert.Equal(7, accountId!.Value);
    }

    [Theory]
    [InlineData(DomainStatus.Banned, false)]
    [InlineData(DomainStatus.Deactivated, false)]
    [InlineData(DomainStatus.Banned, true)]
    [InlineData(DomainStatus.Deactivated, true)]
    public async Task Refuse_an_account_that_is_not_active_as_if_the_password_were_wrong(DomainStatus status, bool mfa)
    {
        AccountIs(status, mfa);
        var wrongPassword = await Assert.ThrowsAsync<AuthenticationException>(() => LoginAsync("wrong"));

        var refused = await Assert.ThrowsAsync<AuthenticationException>(() => LoginAsync(Password));

        Assert.Equal(wrongPassword.Message, refused.Message);
        _jwt.DidNotReceiveWithAnyArgs().GenerateJwtToken(default!);
        await _mfaHash.DidNotReceiveWithAnyArgs().GenerateHashAsync(default!);
        await _accounts.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }
}
