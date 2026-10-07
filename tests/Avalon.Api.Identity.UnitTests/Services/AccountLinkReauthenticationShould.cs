using System.Net;
using Avalon.Api.Identity.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Login;
using Avalon.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.Services;

public class AccountLinkReauthenticationShould
{
    private readonly IReauthentication _password = Substitute.For<IReauthentication>();
    private readonly IMfaSetupRepository _mfa = Substitute.For<IMfaSetupRepository>();
    private readonly IMFAHashService _hashes = Substitute.For<IMFAHashService>();
    private readonly Account _account = new() { Id = new AccountId(7), Username = "PLAYER", Email = "player@example.test", Salt = [1], Verifier = [2], JoinDate = DateTime.UnixEpoch };
    private readonly AccountLinkReauthentication _service;

    public AccountLinkReauthenticationShould()
    {
        var policy = new MfaLoginPolicy(Substitute.For<IAccountRepository>(), Substitute.For<IReplicatedCache>(),
            Substitute.For<ILoginLimits>(), Substitute.For<IMFAService>(), _hashes, NullLoggerFactory.Instance);
        _service = new(_password, _mfa, _hashes, policy);
        _password.RequireCurrentPasswordAsync(_account.Id, "correct", IPAddress.Loopback, Arg.Any<CancellationToken>()).Returns(new Reauthenticated(_account.Id, 0));
    }

    [Fact]
    public async Task Require_current_password_even_with_a_browser_session()
    {
        LinkReauthenticated proof = await _service.RequireAsync(_account, "correct", null, IPAddress.Loopback, CancellationToken.None);
        Assert.Null(proof.Error);
        await _password.Received(1).RequireCurrentPasswordAsync(_account.Id, "correct", IPAddress.Loopback, Arg.Any<CancellationToken>());
        Assert.Null(proof.ConfirmedMfaId);
    }

    [Fact]
    public async Task Refuse_enrolled_MFA_without_a_fresh_code()
    {
        _mfa.FindByAccountIdAsync(_account.Id, Arg.Any<CancellationToken>()).Returns(new MFASetup
        { Id = Guid.NewGuid(), Account = _account, AccountId = _account.Id, Secret = [1], Status = MfaSetupStatus.Confirmed });
        LinkReauthenticated proof = await _service.RequireAsync(_account, "correct", null, IPAddress.Loopback, CancellationToken.None);
        Assert.Equal("MFA_REQUIRED", proof.Error);
        await _hashes.DidNotReceive().GenerateHashAsync(Arg.Any<Account>());
    }

    [Fact]
    public async Task Refuse_password_version_changed_since_browser_authentication()
    {
        _password.RequireCurrentPasswordAsync(_account.Id, "correct", IPAddress.Loopback, Arg.Any<CancellationToken>()).Returns(new Reauthenticated(_account.Id, 1));
        Assert.Equal("ACCOUNT_UNAVAILABLE", (await _service.RequireAsync(_account, "correct", "123456", IPAddress.Loopback, CancellationToken.None)).Error);
        await _hashes.DidNotReceive().GenerateHashAsync(Arg.Any<Account>());
    }
}
