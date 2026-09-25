using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using Avalon.Api.Authentication;
using Avalon.Api.Services;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using static Avalon.Api.UnitTests.Authentication.ApiAuthHost;

namespace Avalon.Api.UnitTests.Authentication;

/// <summary>
/// #480: an access JWT must stop working when it expires, and must never carry more than the
/// account behind it holds now. Every request goes over HTTP through <see cref="ApiAuthHost"/>,
/// so the bearer handler, its events and the policies are the real ones.
/// </summary>
public sealed class JwtRequestAuthenticationShould : IAsyncLifetime
{
    private ApiAuthHost _host = null!;
    private IAccountService Accounts => _host.Accounts;

    public async Task InitializeAsync() => _host = await ApiAuthHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static AccountId IsTheCaller => Arg.Is<AccountId>(id => id.Value == AccountIdValue);

    [Fact]
    public async Task Accept_a_live_token_for_an_active_account_and_load_the_account_once()
    {
        Account account = MakeAccount();
        _host.AccountNowIs(account);

        using HttpResponseMessage response = await _host.GetAsync("/player", Mint(account));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Authentication loads the account; the authorization handler reuses it.
        await Accounts.Received(1).FindByIdAsync(IsTheCaller, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuse_a_token_that_expired_beyond_the_clock_skew()
    {
        _host.AccountNowIs(MakeAccount());
        DateTime now = DateTime.UtcNow;
        // Expired five minutes ago; the configured skew is one minute.
        string token = MintCustom(now.AddMinutes(-20), now.AddMinutes(-5));

        using HttpResponseMessage response = await _host.GetAsync("/player", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Accept_a_token_that_expired_within_the_clock_skew()
    {
        _host.AccountNowIs(MakeAccount());
        DateTime now = DateTime.UtcNow;
        string token = MintCustom(now.AddMinutes(-15), now.AddSeconds(-10));

        using HttpResponseMessage response = await _host.GetAsync("/player", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Deny_the_admin_policy_to_an_admin_token_whose_account_was_demoted()
    {
        string token = Mint(MakeAccount(AccountAccessLevel.Player | AccountAccessLevel.Admin));
        _host.AccountNowIs(MakeAccount(AccountAccessLevel.Player));

        using HttpResponseMessage response = await _host.GetAsync("/admin", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await Accounts.Received().FindByIdAsync(IsTheCaller, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Carry_only_the_roles_both_the_token_and_the_account_hold()
    {
        // Minted as Player|Admin; since then Admin was removed and GameMaster granted. The token
        // loses Admin at once, and does not gain GameMaster until the account signs in again.
        string token = Mint(MakeAccount(AccountAccessLevel.Player | AccountAccessLevel.Admin));
        _host.AccountNowIs(MakeAccount(AccountAccessLevel.Player | AccountAccessLevel.GameMaster));

        using HttpResponseMessage response = await _host.GetAsync("/roles", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Player", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Let_an_admin_token_through_while_the_account_is_still_admin()
    {
        Account account = MakeAccount(AccountAccessLevel.Player | AccountAccessLevel.Admin);
        _host.AccountNowIs(account);

        using HttpResponseMessage response = await _host.GetAsync("/admin", Mint(account));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(AccountStatus.Banned)]
    [InlineData(AccountStatus.Deactivated)]
    public async Task Refuse_a_token_for_an_account_that_is_no_longer_active(AccountStatus status)
    {
        string token = Mint(MakeAccount());
        _host.AccountNowIs(MakeAccount(status: status));

        using HttpResponseMessage response = await _host.GetAsync("/player", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await Accounts.Received().FindByIdAsync(IsTheCaller, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuse_a_token_for_an_account_that_no_longer_exists()
    {
        string token = Mint(MakeAccount());
        _host.AccountNowIs(null);

        using HttpResponseMessage response = await _host.GetAsync("/player", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-number")]
    [InlineData("-7")]
    public async Task Refuse_a_token_whose_subject_is_missing_or_not_an_account_id(string? subject)
    {
        _host.AccountNowIs(MakeAccount());

        using HttpResponseMessage response = await _host.GetAsync("/player", MintLive(subject));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refuse_a_token_signed_with_another_algorithm()
    {
        _host.AccountNowIs(MakeAccount());

        using HttpResponseMessage response =
            await _host.GetAsync("/player", MintLive(algorithm: SecurityAlgorithms.HmacSha512));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Accept_a_token_sent_in_the_session_cookie()
    {
        Account account = MakeAccount();
        _host.AccountNowIs(account);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/player");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={Mint(account)}");
        using HttpResponseMessage response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Refuse_a_non_active_account_through_the_session_cookie_too()
    {
        string token = Mint(MakeAccount());
        _host.AccountNowIs(MakeAccount(status: AccountStatus.Banned));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/player");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={token}");
        using HttpResponseMessage response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // An anonymous endpoint still answers a refused token, but as an anonymous caller.
    [Fact]
    public async Task Treat_a_refused_token_as_anonymous_on_an_anonymous_endpoint()
    {
        string token = Mint(MakeAccount());
        _host.AccountNowIs(MakeAccount(status: AccountStatus.Banned));

        using HttpResponseMessage response = await _host.GetAsync("/anonymous", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("anonymous", await response.Content.ReadAsStringAsync());
    }

    // The database being down must fail closed, and read as "unavailable", not as a server bug.
    [Fact]
    public async Task Answer_503_when_the_account_lookup_hits_a_database_failure()
    {
        Accounts.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new FakeDbException());

        using HttpResponseMessage response = await _host.GetAsync("/player", Mint(MakeAccount()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Never_let_a_request_through_when_the_account_lookup_throws()
    {
        Accounts.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        using HttpResponseMessage response = await _host.GetAsync("/player", Mint(MakeAccount()));

        Assert.False(response.IsSuccessStatusCode);
    }

    private sealed class FakeDbException() : DbException("connection refused");
}
