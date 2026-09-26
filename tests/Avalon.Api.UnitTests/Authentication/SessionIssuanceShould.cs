using System.Net;
using System.Net.Http.Json;
using AuthenticateRequest = Avalon.Api.Contract.AuthenticateRequest;
using RefreshResponse = Avalon.Api.Contract.RefreshResponse;
using Avalon.Api.Exceptions;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using static Avalon.Api.UnitTests.Authentication.ApiAuthHost;

namespace Avalon.Api.UnitTests.Authentication;

/// <summary>
/// #480: the ways a session is handed out — refresh and MFA verify here, password login in
/// <c>AccountLoginStatusShould</c> — must refuse an account that is not Active, or a banned
/// account could keep minting fresh access tokens. Runs the real controllers over HTTP.
/// </summary>
public sealed class SessionIssuanceShould : IAsyncLifetime
{
    private const string RefreshCookie = "refresh-raw";

    private ApiAuthHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiAuthHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private void RefreshCookieBelongsTo(Account? account)
    {
        _host.Refresh.RotateAsync(RefreshCookie, Arg.Any<CancellationToken>())
            .Returns(new RefreshRotateResult("refresh-next", DateTime.UtcNow.AddDays(30), new AccountId(AccountIdValue), 0));
        _host.AccountRepository.FindByIdAsync(Arg.Is<AccountId>(id => id.Value == AccountIdValue), Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(account);
    }

    private async Task<HttpResponseMessage> PostRefreshAsync(string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/refresh");
        if (bearer is not null)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.Add("Cookie", $"{AuthConfig.RefreshCookieName}={RefreshCookie}");
        return await _host.Client.SendAsync(request);
    }

    private static bool SetsRefreshCookie(HttpResponseMessage response, string value) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
        && cookies.Any(c => c.StartsWith($"{AuthConfig.RefreshCookieName}={value}", StringComparison.Ordinal));

    // Lifetime validation must not lock a client out of renewing: refresh is anonymous and reads
    // only the refresh cookie, so an expired access token sent alongside it is no obstacle, and
    // the token it returns is accepted.
    [Fact]
    public async Task Refresh_with_an_expired_access_token_and_a_valid_refresh_cookie()
    {
        Account account = MakeAccount();
        _host.AccountNowIs(account);
        RefreshCookieBelongsTo(account);
        DateTime now = DateTime.UtcNow;

        using HttpResponseMessage refreshed =
            await PostRefreshAsync(MintCustom(now.AddMinutes(-20), now.AddMinutes(-5)));

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.True(SetsRefreshCookie(refreshed, "refresh-next"));
        RefreshResponse? body = await refreshed.Content.ReadFromJsonAsync<RefreshResponse>();
        Assert.False(string.IsNullOrEmpty(body?.Token));

        using HttpResponseMessage response = await _host.GetAsync("/player", body!.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// #495 review: the second of two tabs refreshing at once, inside the grace window. It is told
    /// 401, but the refresh cookie the tabs share (now the winner's) is not cleared, and nothing
    /// ends the account's sessions.
    /// </summary>
    [Fact]
    public async Task Leave_the_cookie_and_the_sessions_alone_for_a_refresh_that_lost_a_race()
    {
        _host.Refresh.RotateAsync(RefreshCookie, Arg.Any<CancellationToken>())
            .Returns<RefreshRotateResult>(_ => throw new RefreshAlreadyRotatedException());

        using HttpResponseMessage response = await PostRefreshAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        await _host.Cache.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!);
    }

    [Theory]
    [InlineData(AccountStatus.Banned)]
    [InlineData(AccountStatus.Deactivated)]
    public async Task Refuse_to_refresh_an_account_that_is_not_active(AccountStatus status)
    {
        RefreshCookieBelongsTo(MakeAccount(status: status));

        using HttpResponseMessage response = await PostRefreshAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("token", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.False(SetsRefreshCookie(response, "refresh-next"));
        // The rotation already minted a successor; it and every other refresh token must die.
        await _host.Refresh.Received(1).RevokeAllForAccountAsync(
            Arg.Is<AccountId>(id => id.Value == AccountIdValue), Arg.Any<CancellationToken>());
    }

    private void MfaCodeIsValid(Account? account)
    {
        _host.Mfa.VerifyMFAAsync("hash", "123456", Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(true, new AccountId(AccountIdValue)));
        _host.AccountRepository.FindByIdAsync(Arg.Is<AccountId>(id => id.Value == AccountIdValue), Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(account);
        _host.Refresh.IssueAsync(Arg.Any<AccountId>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new RefreshIssueResult("refresh-new", DateTime.UtcNow.AddDays(30), Guid.NewGuid()));
    }

    private Task<HttpResponseMessage> PostVerifyAsync() =>
        _host.Client.PostAsJsonAsync("/mfa/verify", new { hash = "hash", code = "123456" });

    [Fact]
    public async Task Issue_a_session_on_mfa_verify_for_an_active_account()
    {
        MfaCodeIsValid(MakeAccount());

        using HttpResponseMessage response = await PostVerifyAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(SetsRefreshCookie(response, "refresh-new"));
    }

    [Theory]
    [InlineData(AccountStatus.Banned, "BANNED")]
    [InlineData(AccountStatus.Deactivated, "DEACTIVATED")]
    public async Task Tell_mfa_verify_for_an_account_that_is_not_active_its_status(AccountStatus status, string expected)
    {
        MfaCodeIsValid(MakeAccount(status: status));

        using HttpResponseMessage response = await PostVerifyAsync();

        await AssertInactive(response, expected);
        Assert.False(SetsRefreshCookie(response, "refresh-new"));
        await _host.Refresh.DidNotReceiveWithAnyArgs().IssueAsync(default!, default, default);
        await _host.AccountRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    // A bad code says nothing about the account, whatever its status.
    [Fact]
    public async Task Give_a_bad_mfa_code_the_generic_answer()
    {
        _host.Mfa.VerifyMFAAsync("hash", "123456", Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(false, null));

        using HttpResponseMessage response = await PostVerifyAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("BANNED", body, StringComparison.Ordinal);
        Assert.DoesNotContain("DEACTIVATED", body, StringComparison.Ordinal);
    }

    // The login endpoint turns the service's refusal into the same 403, and issues no refresh token.
    [Theory]
    [InlineData(AccountStatus.Banned, "BANNED")]
    [InlineData(AccountStatus.Deactivated, "DEACTIVATED")]
    public async Task Answer_login_for_an_account_that_is_not_active_with_403_and_its_status(
        AccountStatus status, string expected)
    {
        _host.Accounts.Authenticate(Arg.Any<AuthenticateRequest>(), Arg.Any<System.Net.IPAddress>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AccountInactiveException(status));

        using HttpResponseMessage response = await _host.Client.PostAsJsonAsync("/account/authenticate",
            new { username = "caller", password = "right" });

        await AssertInactive(response, expected);
        await _host.Refresh.DidNotReceiveWithAnyArgs().IssueAsync(default!, default, default);
    }

    private static async Task AssertInactive(HttpResponseMessage response, string expected)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(403, problem?.Status);
        Assert.Equal(expected, problem?.Detail);
        Assert.False(SetsAnyCookie(response));
    }

    private static bool SetsAnyCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any();
}
