using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Avalon.Api.Authentication;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using RefreshResponse = Avalon.Api.Contract.RefreshResponse;
using Avalon.Api.Controllers;
using Avalon.Api.Middlewares;
using Avalon.Api.Services;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Authentication;

/// <summary>
/// #480: an access JWT must stop working when it expires, and must never carry more than the
/// account behind it holds now. Every request here goes over HTTP through an in-memory host
/// built with the API's own <see cref="ServiceRegistration.AddAuth"/> and the same middleware
/// order as Program.cs, so the bearer handler, its events and the policies are the real ones.
/// Tokens are minted by the real <see cref="JwtUtils"/>, or with the same key when a test needs
/// to choose the lifetime itself.
/// </summary>
public sealed class JwtRequestAuthenticationShould : IAsyncLifetime
{
    private const long AccountIdValue = 7;
    private const string SigningKey = "test-signing-key-test-signing-key-test-signing-key-0123456789";

    private static readonly AuthenticationConfig AuthConfig = new()
    {
        IssuerSigningKey = SigningKey,
        ValidateIssuerKey = true,
        Issuer = "Avalon Authentication System",
        ValidateIssuer = true,
        Audience = "https://api.avalon.monster",
        ValidateAudience = true,
        ClockSkewInMinutes = 1,
        AccessTokenLifetimeMinutes = 15,
    };

    private const string RefreshCookie = "refresh-raw";

    private readonly IAccountService _accounts = Substitute.For<IAccountService>();
    private readonly IAccountRepository _accountRepository = Substitute.For<IAccountRepository>();
    private readonly IRefreshTokenService _refresh = Substitute.For<IRefreshTokenService>();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        IServiceCollection services = builder.Services;
        services.AddHttpContextAccessor();
        services.AddSingleton(_accounts);
        services.AddSingleton(Substitute.For<IPersonalAccessTokenService>());
        services.AddAuth(new ApplicationConfig { Authentication = AuthConfig });
        // What the real refresh controller needs, so POST /account/refresh runs as it does in the api.
        services.AddControllers().AddApplicationPart(typeof(AccountRefreshController).Assembly);
        services.AddSingleton(AuthConfig);
        services.AddSingleton(_refresh);
        services.AddSingleton(_accountRepository);
        services.AddSingleton(Substitute.For<IReplicatedCache>());
        services.AddSingleton<IJwtUtils>(new JwtUtils(AuthConfig));

        _app = builder.Build();
        _app.UseMiddleware<ExceptionHandlerMiddleware>();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapGet("/player", () => "ok").RequireAuthorization(AvalonRoles.Player);
        _app.MapGet("/admin", () => "ok").RequireAuthorization(AvalonRoles.Admin);
        _app.MapGet("/roles", (HttpContext http) => string.Join(",", http.User
                .FindAll(ClaimTypes.GroupSid).Select(c => c.Value).Order(StringComparer.Ordinal)))
            .RequireAuthorization(AvalonRoles.Player);
        _app.MapControllers();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private static Account MakeAccount(AccountAccessLevel level = AccountAccessLevel.Player,
        AccountStatus status = AccountStatus.Active) => new()
    {
        Id = new AccountId(AccountIdValue), Username = "CALLER", Email = "caller@avalon.monster",
        Salt = [1], Verifier = [2], JoinDate = DateTime.UtcNow, AccessLevel = level, Status = status,
    };

    private void AccountNowIs(Account? account) =>
        _accounts.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>()).Returns(account);

    private static string Mint(Account account) => new JwtUtils(AuthConfig).GenerateJwtToken(account);

    // The claims JwtUtils writes for a Player, with a lifetime the test chooses.
    private static string MintWithLifetime(Account account, DateTime notBefore, DateTime expires)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(JwtRegisteredClaimNames.Name, account.Username),
            new(JwtRegisteredClaimNames.Email, account.Email),
            new(ClaimTypes.GroupSid, nameof(AccountAccessLevel.Player)),
        };
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(SigningKey));
        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = notBefore,
            IssuedAt = notBefore,
            Expires = expires,
            Issuer = AuthConfig.Issuer,
            Audience = AuthConfig.Audience,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature),
        }));
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Accept_a_live_token_for_an_active_account()
    {
        Account account = MakeAccount();
        AccountNowIs(account);

        using HttpResponseMessage response = await GetAsync("/player", Mint(account));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Refuse_a_token_that_expired_beyond_the_clock_skew()
    {
        Account account = MakeAccount();
        AccountNowIs(account);
        DateTime now = DateTime.UtcNow;
        // Expired five minutes ago; the configured skew is one minute.
        string token = MintWithLifetime(account, now.AddMinutes(-20), now.AddMinutes(-5));

        using HttpResponseMessage response = await GetAsync("/player", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Accept_a_token_that_expired_within_the_clock_skew()
    {
        Account account = MakeAccount();
        AccountNowIs(account);
        DateTime now = DateTime.UtcNow;
        string token = MintWithLifetime(account, now.AddMinutes(-15), now.AddSeconds(-10));

        using HttpResponseMessage response = await GetAsync("/player", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Deny_the_admin_policy_to_an_admin_token_whose_account_was_demoted()
    {
        string token = Mint(MakeAccount(AccountAccessLevel.Player | AccountAccessLevel.Admin));
        AccountNowIs(MakeAccount(AccountAccessLevel.Player));

        using HttpResponseMessage response = await GetAsync("/admin", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Carry_only_the_roles_both_the_token_and_the_account_hold()
    {
        // Minted as Player|Admin; since then Admin was removed and GameMaster granted. The token
        // loses Admin at once, and does not gain GameMaster until the account signs in again.
        string token = Mint(MakeAccount(AccountAccessLevel.Player | AccountAccessLevel.Admin));
        AccountNowIs(MakeAccount(AccountAccessLevel.Player | AccountAccessLevel.GameMaster));

        using HttpResponseMessage response = await GetAsync("/roles", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Player", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Let_an_admin_token_through_while_the_account_is_still_admin()
    {
        Account account = MakeAccount(AccountAccessLevel.Player | AccountAccessLevel.Admin);
        AccountNowIs(account);

        using HttpResponseMessage response = await GetAsync("/admin", Mint(account));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(AccountStatus.Banned)]
    [InlineData(AccountStatus.Deactivated)]
    public async Task Refuse_a_token_for_an_account_that_is_no_longer_active(AccountStatus status)
    {
        string token = Mint(MakeAccount());
        AccountNowIs(MakeAccount(status: status));

        using HttpResponseMessage response = await GetAsync("/player", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refuse_a_token_for_an_account_that_no_longer_exists()
    {
        string token = Mint(MakeAccount());
        AccountNowIs(null);

        using HttpResponseMessage response = await GetAsync("/player", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Lifetime validation must not lock a client out of renewing: refresh is anonymous and reads
    // only the refresh cookie, so an expired access token sent alongside it is no obstacle, and
    // the token it returns is accepted.
    [Fact]
    public async Task Refresh_with_an_expired_access_token_and_a_valid_refresh_cookie()
    {
        Account account = MakeAccount();
        AccountNowIs(account);
        _refresh.RotateAsync(RefreshCookie, Arg.Any<CancellationToken>())
            .Returns(new RefreshRotateResult("refresh-next", DateTime.UtcNow.AddDays(30), account.Id));
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(account);
        DateTime now = DateTime.UtcNow;
        string expired = MintWithLifetime(account, now.AddMinutes(-20), now.AddMinutes(-5));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/refresh");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expired);
        request.Headers.Add("Cookie", $"{AuthConfig.RefreshCookieName}={RefreshCookie}");
        using HttpResponseMessage refreshed = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        RefreshResponse? body = await refreshed.Content.ReadFromJsonAsync<RefreshResponse>();
        Assert.False(string.IsNullOrEmpty(body?.Token));

        using HttpResponseMessage response = await GetAsync("/player", body!.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
