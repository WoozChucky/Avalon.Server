using System.Net;
using System.Reflection;
using System.Security.Authentication;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Api.Controllers;
using Avalon.Api.Exceptions;
using Avalon.Api.Middlewares;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using AccountStatus = Avalon.Domain.Auth.AccountStatus;
using Avalon.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Avalon.Api.UnitTests.Controllers;

/// <summary>
/// Launcher sign-in (#591): the website issues a code for the launcher's PKCE challenge; the launcher
/// trades it for tokens of its own, refreshes them by rotation, and signs out. Tokens travel in bodies.
/// </summary>
public class ClientAuthControllerShould
{
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

    private readonly ILauncherAuthCodes _codes = Substitute.For<ILauncherAuthCodes>();
    private readonly IRefreshTokenService _refresh = Substitute.For<IRefreshTokenService>();
    private readonly IJwtUtils _jwt = Substitute.For<IJwtUtils>();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly DefaultHttpContext _http = new();

    public ClientAuthControllerShould()
    {
        _jwt.GenerateJwtToken(Arg.Any<Account>()).Returns("jwt");
        _http.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.7");
    }

    private ClientAuthController Sut(Account? signedIn = null)
    {
        if (signedIn is not null) _http.Items[nameof(Account)] = signedIn;
        return new ClientAuthController(_codes, _refresh, _jwt, _accounts,
            new AuthenticationConfig { AccessTokenLifetimeMinutes = 15 }, _cache, new ForwardedHeadersOptions())
        {
            ControllerContext = new ControllerContext { HttpContext = _http },
        };
    }

    private static Account MakeAccount(int credentialsVersion = 3, AccountStatus status = AccountStatus.Active) => new()
    {
        Id = new AccountId(7L),
        Username = "TESTER",
        Salt = [],
        Verifier = [],
        Email = "tester@example.com",
        JoinDate = DateTime.UtcNow,
        CredentialsVersion = credentialsVersion,
        Status = status,
    };

    private void AccountIs(Account? account) =>
        _accounts.FindByIdAsync(new AccountId(7L), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(account);

    private static void AssertInvalidGrant(IActionResult result)
    {
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("invalid_grant", Assert.IsType<ProblemDetails>(problem.Value).Title);
    }

    [Fact]
    public async Task Issue_a_code_for_the_signed_in_account()
    {
        _codes.IssueAsync(new AccountId(7L), 3, Challenge, 50000).Returns("the-code");

        IActionResult result = await Sut(MakeAccount()).Code(new ClientAuthCodeRequest { Challenge = Challenge, RedirectPort = 50000 });

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("the-code", Assert.IsType<ClientAuthCodeResponse>(ok.Value).Code);
    }

    [Fact]
    public async Task Refuse_a_bad_challenge_with_400()
    {
        _codes.IssueAsync(Arg.Any<AccountId>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>())
            .ThrowsAsync(new ArgumentException("bad challenge"));

        IActionResult result = await Sut(MakeAccount()).Code(new ClientAuthCodeRequest { Challenge = "x", RedirectPort = 50000 });

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [Fact]
    public async Task Exchange_a_code_for_launcher_tokens_in_the_body()
    {
        _codes.RedeemAsync("the-code", Verifier).Returns(new LauncherGrant(new AccountId(7L), 3, 50000));
        AccountIs(MakeAccount());
        DateTime refreshExpiry = new(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);
        _refresh.IssueLauncherAsync(new AccountId(7L), 3, "MOTHERSHIP", Arg.Any<CancellationToken>())
            .Returns(new RefreshIssueResult("refresh-1", refreshExpiry, Guid.NewGuid()));

        IActionResult result = await Sut().Token(new ClientAuthTokenRequest { Code = "the-code", Verifier = Verifier, DeviceName = "MOTHERSHIP" });

        var tokens = Assert.IsType<ClientAuthTokens>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("jwt", tokens.AccessToken);
        Assert.Equal("refresh-1", tokens.RefreshToken);
        Assert.Equal(new DateTimeOffset(refreshExpiry).ToUnixTimeSeconds(), tokens.RefreshExpiresAt);
        Assert.True(tokens.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Assert.False(_http.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task Refuse_an_unknown_or_spent_code_with_invalid_grant()
    {
        _codes.RedeemAsync(Arg.Any<string>(), Arg.Any<string>()).Returns((LauncherGrant?)null);

        AssertInvalidGrant(await Sut().Token(new ClientAuthTokenRequest { Code = "spent", Verifier = Verifier }));
        await _refresh.DidNotReceiveWithAnyArgs().IssueLauncherAsync(default, default, default);
    }

    [Fact]
    public async Task Refuse_a_code_issued_before_a_credentials_change()
    {
        _codes.RedeemAsync("the-code", Verifier).Returns(new LauncherGrant(new AccountId(7L), 3, 50000));
        AccountIs(MakeAccount(credentialsVersion: 4));

        AssertInvalidGrant(await Sut().Token(new ClientAuthTokenRequest { Code = "the-code", Verifier = Verifier }));
        await _refresh.DidNotReceiveWithAnyArgs().IssueLauncherAsync(default, default, default);
    }

    [Fact]
    public async Task Refuse_a_code_for_an_account_that_may_not_hold_a_session()
    {
        _codes.RedeemAsync("the-code", Verifier).Returns(new LauncherGrant(new AccountId(7L), 3, 50000));
        AccountIs(MakeAccount(status: AccountStatus.Banned));

        AssertInvalidGrant(await Sut().Token(new ClientAuthTokenRequest { Code = "the-code", Verifier = Verifier }));
    }

    [Fact]
    public async Task Refuse_a_code_when_the_credentials_move_while_issuing()
    {
        _codes.RedeemAsync("the-code", Verifier).Returns(new LauncherGrant(new AccountId(7L), 3, 50000));
        AccountIs(MakeAccount());
        _refresh.IssueLauncherAsync(Arg.Any<AccountId>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AuthenticationException("Credentials changed"));

        AssertInvalidGrant(await Sut().Token(new ClientAuthTokenRequest { Code = "the-code", Verifier = Verifier }));
    }

    [Fact]
    public async Task Rotate_a_launcher_session_and_return_the_new_pair_in_the_body()
    {
        DateTime expiry = new(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);
        _refresh.RotateLauncherAsync("refresh-1", Arg.Any<RefreshCaller>(), Arg.Any<CancellationToken>())
            .Returns(new RefreshRotateResult("refresh-2", expiry, new AccountId(7L), 3));
        AccountIs(MakeAccount());

        IActionResult result = await Sut().Refresh(new ClientAuthRefreshRequest { RefreshToken = "refresh-1" });

        var tokens = Assert.IsType<ClientAuthTokens>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("refresh-2", tokens.RefreshToken);
        Assert.Equal("jwt", tokens.AccessToken);
        await _refresh.DidNotReceiveWithAnyArgs().RotateAsync(default!, default!);
    }

    [Fact]
    public async Task Refuse_a_reused_refresh_token_and_disconnect_the_account()
    {
        _refresh.RotateLauncherAsync(Arg.Any<string>(), Arg.Any<RefreshCaller>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RefreshTheftException(new AccountId(7L)));

        IActionResult result = await Sut().Refresh(new ClientAuthRefreshRequest { RefreshToken = "old" });

        Assert.IsType<UnauthorizedResult>(result);
        await _cache.Received(1).PublishAsync(CacheKeys.WorldAccountsDisconnectChannel, "7");
    }

    [Fact]
    public async Task Refuse_a_refresh_for_an_account_that_may_not_hold_a_session()
    {
        _refresh.RotateLauncherAsync(Arg.Any<string>(), Arg.Any<RefreshCaller>(), Arg.Any<CancellationToken>())
            .Returns(new RefreshRotateResult("refresh-2", DateTime.UtcNow.AddDays(1), new AccountId(7L), 3));
        AccountIs(MakeAccount(status: AccountStatus.Banned));

        Assert.IsType<UnauthorizedResult>(await Sut().Refresh(new ClientAuthRefreshRequest { RefreshToken = "refresh-1" }));
        await _refresh.Received(1).RevokeAllForAccountAsync(new AccountId(7L), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuse_a_refresh_whose_credentials_changed_after_the_rotation()
    {
        _refresh.RotateLauncherAsync(Arg.Any<string>(), Arg.Any<RefreshCaller>(), Arg.Any<CancellationToken>())
            .Returns(new RefreshRotateResult("refresh-2", DateTime.UtcNow.AddDays(1), new AccountId(7L), 3));
        AccountIs(MakeAccount(credentialsVersion: 4));

        Assert.IsType<UnauthorizedResult>(await Sut().Refresh(new ClientAuthRefreshRequest { RefreshToken = "refresh-1" }));
    }

    [Fact]
    public async Task Answer_an_unknown_or_other_client_refresh_token_with_401()
    {
        _refresh.RotateLauncherAsync(Arg.Any<string>(), Arg.Any<RefreshCaller>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new UnauthorizedAccessException("Unknown refresh token"));

        Assert.IsType<UnauthorizedResult>(await Sut().Refresh(new ClientAuthRefreshRequest { RefreshToken = "web-token" }));
        await _cache.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!);
    }

    [Fact]
    public async Task Revoke_the_whole_session_and_answer_204_even_for_an_unknown_token()
    {
        IActionResult result = await Sut().Revoke(new ClientAuthRefreshRequest { RefreshToken = "refresh-1" });

        Assert.IsType<NoContentResult>(result);
        await _refresh.Received(1).RevokeFamilyAsync("refresh-1", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(nameof(ClientAuthController.Code))]
    [InlineData(nameof(ClientAuthController.Token))]
    [InlineData(nameof(ClientAuthController.Refresh))]
    [InlineData(nameof(ClientAuthController.Revoke))]
    public void Limit_every_sign_in_step_with_the_client_auth_policy(string action)
    {
        EnableRateLimitingAttribute? attribute = typeof(ClientAuthController).GetMethod(action)!
            .GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.Equal(ApiRateLimiting.ClientAuthPolicy, attribute?.PolicyName);
    }
}
