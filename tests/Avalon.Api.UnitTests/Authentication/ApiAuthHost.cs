using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Avalon.Api.Authentication;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Controllers;
using Avalon.Api.Middlewares;
using Avalon.Api.Services;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Extensions;
using Avalon.Infrastructure.Login;
using Avalon.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Avalon.Api.UnitTests.Authentication;

/// <summary>
/// An in-memory api: the real <see cref="ServiceRegistration.AddAuth"/>, the real controllers and
/// the middleware order of Program.cs, with every service below the controllers substituted.
/// Requests go over HTTP, so the bearer handler, its events, the policies and
/// <see cref="AvalonAuthHandler"/> all run as they do in production. A few minimal endpoints stand
/// in for "any endpoint behind policy X".
/// </summary>
public sealed class ApiAuthHost : IAsyncDisposable
{
    public const long AccountIdValue = 7;

    /// <summary>A request carrying this header reaches the api with no peer address.</summary>
    public const string NoAddressHeader = "X-Test-No-Address";
    public const string SigningKey = "test-signing-key-test-signing-key-test-signing-key-0123456789-abcdef";

    public static readonly AuthenticationConfig AuthConfig = new()
    {
        IssuerSigningKey = SigningKey,
        Issuer = "Avalon Authentication System",
        ValidateIssuer = true,
        Audience = "https://api.avalon.monster",
        ValidateAudience = true,
        ClockSkewInMinutes = 1,
        AccessTokenLifetimeMinutes = 15,
    };

    public IAccountService Accounts { get; } = Substitute.For<IAccountService>();
    public IAccountRepository AccountRepository { get; } = Substitute.For<IAccountRepository>();
    public IRefreshTokenService Refresh { get; } = Substitute.For<IRefreshTokenService>();
    public IMFAService Mfa { get; } = Substitute.For<IMFAService>();
    public IMFAHashService MfaHashes { get; } = Substitute.For<IMFAHashService>();
    public IPersonalAccessTokenService Pats { get; } = Substitute.For<IPersonalAccessTokenService>();
    public IReplicatedCache Cache { get; private set; } = Substitute.For<IReplicatedCache>();

    private WebApplication _app = null!;
    public HttpClient Client { get; private set; } = null!;

    /// <param name="cache">The cache the login policy counts on; a plain substitute when not given.</param>
    public static async Task<ApiAuthHost> StartAsync(IReplicatedCache? cache = null)
    {
        var host = new ApiAuthHost();
        if (cache != null) host.Cache = cache;
        await host.InitializeAsync();
        return host;
    }

    private async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        IServiceCollection services = builder.Services;
        services.AddHttpContextAccessor();
        services.AddSingleton(Accounts);
        services.AddSingleton(Pats);
        services.AddAuth(new ApplicationConfig { Authentication = AuthConfig });
        services.AddControllers().AddApplicationPart(typeof(AccountRefreshController).Assembly);
        services.AddSingleton(AuthConfig);
        services.AddSingleton(Refresh);
        services.AddSingleton(AccountRepository);
        services.AddSingleton(Mfa);
        services.AddSingleton(Cache);
        // The real login policy (#478) over the substitutes above: a live hash for the account, a
        // first attempt on it, and a login record that succeeds, unless a test says otherwise.
        services.AddSingleton<ILoginLimits>(AuthConfig);
        services.AddSingleton(MfaHashes);
        services.AddScoped<IReauthentication, Reauthentication>();
        services.AddLoginPolicy();
        MfaHashes.GetAccountIdAsync(Arg.Any<string>()).Returns(new AccountId(AccountIdValue));
        MfaHashes.RecordAttemptAsync(Arg.Any<AccountId>()).Returns(1L);
        AccountRepository.TryRecordApiLoginAsync(Arg.Any<AccountId>(), Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>()).Returns(true);
        services.AddSingleton<IJwtUtils>(new JwtUtils(AuthConfig, JwtSigningKey.Create(AuthConfig)));

        _app = builder.Build();
        // The test server has no socket, so no peer address; a real connection always has one.
        // Loopback stands in, unless a request asks to be the address-less caller.
        _app.Use((context, next) =>
        {
            if (!context.Request.Headers.ContainsKey(NoAddressHeader))
                context.Connection.RemoteIpAddress ??= System.Net.IPAddress.Loopback;
            return next(context);
        });
        _app.UseMiddleware<ExceptionHandlerMiddleware>();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapGet("/player", () => "ok").RequireAuthorization(AvalonRoles.Player);
        _app.MapGet("/admin", () => "ok").RequireAuthorization(AvalonRoles.Admin);
        _app.MapGet("/roles", (HttpContext http) => string.Join(",", http.User
                .FindAll(ClaimTypes.GroupSid).Select(c => c.Value).Order(StringComparer.Ordinal)))
            .RequireAuthorization(AvalonRoles.Player);
        _app.MapGet("/anonymous", (HttpContext http) => http.User.Identity?.IsAuthenticated == true ? "user" : "anonymous")
            .AllowAnonymous();
        _app.MapControllers();

        await _app.StartAsync();
        Client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }

    public static Account MakeAccount(AccountAccessLevel level = AccountAccessLevel.Player,
        AccountStatus status = AccountStatus.Active) => new()
    {
        Id = new AccountId(AccountIdValue), Username = "CALLER", Email = "caller@avalon.monster",
        Salt = [1], Verifier = [2], JoinDate = DateTime.UtcNow, AccessLevel = level, Status = status,
    };

    /// <summary>What the account service returns for exactly <see cref="AccountIdValue"/>, and nothing else.</summary>
    public void AccountNowIs(Account? account) =>
        Accounts.FindByIdAsync(Arg.Is<AccountId>(id => id.Value == AccountIdValue), Arg.Any<CancellationToken>())
            .Returns(account);

    public static string Mint(Account account) => new JwtUtils(AuthConfig, JwtSigningKey.Create(AuthConfig)).GenerateJwtToken(account);

    /// <summary>
    /// The claims JwtUtils writes for a Player, with the lifetime, subject and algorithm the test
    /// chooses. A null subject leaves the name-identifier claim out.
    /// </summary>
    public static string MintCustom(DateTime notBefore, DateTime expires, string? subject = "7",
        string algorithm = SecurityAlgorithms.HmacSha256Signature, string? credentialsVersion = "0")
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Name, "CALLER"),
            new(ClaimTypes.GroupSid, nameof(AccountAccessLevel.Player)),
        };
        if (credentialsVersion is not null) claims.Add(new Claim(JwtUtils.CredentialsVersionClaim, credentialsVersion));
        if (subject is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, subject));

        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = notBefore,
            IssuedAt = notBefore,
            Expires = expires,
            Issuer = AuthConfig.Issuer,
            Audience = AuthConfig.Audience,
            SigningCredentials = new SigningCredentials(key, algorithm),
        }));
    }

    public static string MintLive(string? subject = "7", string algorithm = SecurityAlgorithms.HmacSha256Signature) =>
        MintCustom(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(10), subject, algorithm);

    public async Task<HttpResponseMessage> GetAsync(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }
}
