using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using Avalon.Api.Authentication;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Hosting;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Authentication.AV;
using Avalon.Api.Hosting.Authentication.Jwt;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Services;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Avalon.Api.Testing;

/// <summary>
/// An in-memory API built as the API builds itself (#794): <see cref="AvalonApiHost.CreateBuilder(WebApplicationOptions, IReadOnlyList{IApiService}, Action{WebApplicationBuilder}?)"/>
/// for the services it is given (the monolith unless a test names others) and the one pipeline,
/// <see cref="ApiPipeline"/>, on a test server. What the services reach outside the process is substituted by default:
/// the account and personal access token repositories, the cache, and the account, refresh, MFA and token services.
/// Requests go over HTTP, so the bearer handler, its events, the policies and <see cref="AvalonAuthHandler"/> all run
/// as they do in production. A few minimal endpoints stand in for "any endpoint behind policy X".
/// </summary>
public sealed class ApiTestHost : IAsyncDisposable
{
    public const long AccountIdValue = 7;

    /// <summary>A request carrying this header reaches the api with no peer address.</summary>
    public const string NoAddressHeader = "X-Test-No-Address";

    /// <summary>A request carrying this header reaches the api from the address it names.</summary>
    public const string PeerHeader = "X-Test-Peer";

    /// <summary>With <see cref="ApiTestHostOptions.ProbeRoutes"/>, the route of the endpoint a request matched.</summary>
    public const string MatchedRouteHeader = "X-Matched-Route";

    /// <summary>An endpoint under the launcher sign-in rate-limit policy (#591).</summary>
    public const string ClientAuthLimitedPath = "/client-auth-limited";
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

    /// <summary>
    /// The settings every test host starts with: the token validation's (<see cref="AuthConfig"/>), and what the
    /// shared hosting checks when it starts. The auth database and the cache they name are never reached: their
    /// clients are substituted.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["Application:Authentication:IssuerSigningKey"] = AuthConfig.IssuerSigningKey,
        ["Application:Authentication:Issuer"] = AuthConfig.Issuer,
        ["Application:Authentication:ValidateIssuer"] = "true",
        ["Application:Authentication:Audience"] = AuthConfig.Audience,
        ["Application:Authentication:ValidateAudience"] = "true",
        ["Application:Authentication:ClockSkewInMinutes"] = "1",
        ["Application:Authentication:AccessTokenLifetimeMinutes"] = "15",
        ["Database:Auth:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=none",
        ["Application:Cache:Host"] = "127.0.0.1:1",
        ["Application:Templates:ReloadTimeout"] = "00:00:10",
        // Sections the monolith registers as they are bound, as appsettings.json has them.
        ["Application:Environment:Name"] = "Development",
        ["Application:Notification:Subject"] = "https://avalon.monster",
    };

    public IAccountService Accounts { get; } = Substitute.For<IAccountService>();
    public IAccountRepository AccountRepository { get; } = Substitute.For<IAccountRepository>();
    public IPersonalAccessTokenRepository PatRepository { get; } = Substitute.For<IPersonalAccessTokenRepository>();
    public IRefreshTokenService Refresh { get; } = Substitute.For<IRefreshTokenService>();
    public IMFAService Mfa { get; } = Substitute.For<IMFAService>();
    public IMFAHashService MfaHashes { get; } = Substitute.For<IMFAHashService>();
    public IPersonalAccessTokenService Pats { get; } = Substitute.For<IPersonalAccessTokenService>();
    public IReplicatedCache Cache { get; private set; } = Substitute.For<IReplicatedCache>();

    private WebApplication _app = null!;
    public HttpClient Client { get; private set; } = null!;

    /// <summary>The host's services.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>Every endpoint the host maps, as routing sees them.</summary>
    public IReadOnlyList<Endpoint> Endpoints => _app.Services.GetRequiredService<EndpointDataSource>().Endpoints;

    /// <summary>The monolith.</summary>
    /// <param name="cache">The cache the login policy counts on; a plain substitute when not given.</param>
    /// <param name="configure">
    /// Runs after the host's own registrations, so a test can put a real service, or an email
    /// sender, in place of a substitute.
    /// </param>
    public static Task<ApiTestHost> StartAsync(IReplicatedCache? cache = null, Action<IServiceCollection>? configure = null) =>
        StartAsync([MonolithApi.Service], new ApiTestHostOptions { Cache = cache, Configure = configure });

    /// <summary>The services <paramref name="services"/> names, in that order.</summary>
    public static async Task<ApiTestHost> StartAsync(IReadOnlyList<IApiService> services, ApiTestHostOptions? options = null)
    {
        options ??= new ApiTestHostOptions();
        var host = new ApiTestHost();
        if (options.Cache is not null) host.Cache = options.Cache;
        await host.InitializeAsync(services, options);
        return host;
    }

    private async Task InitializeAsync(IReadOnlyList<IApiService> services, ApiTestHostOptions options)
    {
        // The probe comes first, so its middleware runs before any service's own.
        IReadOnlyList<IApiService> running = options.ProbeRoutes ? [RouteProbe.Service, .. services] : services;
        WebApplicationBuilder builder = AvalonApiHost.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production }, running, b =>
            {
                b.WebHost.UseTestServer();
                b.Configuration.AddInMemoryCollection(Settings);
                if (options.Settings is not null)
                    b.Configuration.AddInMemoryCollection(options.Settings);
            });
        builder.Logging.ClearProviders();
        AddSubstitutes(builder.Services);
        options.Configure?.Invoke(builder.Services);

        _app = builder.Build();
        // The test server has no socket, so no peer address; a real connection always has one.
        // Loopback stands in, unless a request asks to be the address-less caller.
        _app.Use((context, next) =>
        {
            if (context.Request.Headers.TryGetValue(PeerHeader, out StringValues peer))
                context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(peer.ToString());
            else if (!context.Request.Headers.ContainsKey(NoAddressHeader))
                context.Connection.RemoteIpAddress ??= System.Net.IPAddress.Loopback;
            return next(context);
        });
        _app.UseAvalonApi(running);
        MapStandIns(_app);

        await _app.StartAsync();
        Client = _app.GetTestClient();
    }

    /// <summary>
    /// The substitutes, registered after the services' own registrations so they take their place: the real login
    /// policy (#478) then runs over them, with a live hash for the account, a first attempt on it, and a login record
    /// that succeeds, unless a test says otherwise.
    /// </summary>
    private void AddSubstitutes(IServiceCollection services)
    {
        services.AddSingleton(Accounts);
        services.AddSingleton(Pats);
        services.AddSingleton(AuthConfig);
        services.AddSingleton(Refresh);
        services.AddSingleton(AccountRepository);
        services.AddSingleton(PatRepository);
        services.AddSingleton(Mfa);
        services.AddSingleton(Cache);
        services.AddSingleton(MfaHashes);
        MfaHashes.GetAccountIdAsync(Arg.Any<string>()).Returns(new AccountId(AccountIdValue));
        MfaHashes.RecordAttemptAsync(Arg.Any<AccountId>()).Returns(1L);
        AccountRepository.TryRecordApiLoginAsync(Arg.Any<AccountId>(), Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>()).Returns(true);
    }

    private static void MapStandIns(WebApplication app)
    {
        app.MapGet("/player", () => "ok").RequireAuthorization(AvalonRoles.Player);
        app.MapGet("/admin", () => "ok").RequireAuthorization(AvalonRoles.Admin);
        app.MapGet("/roles", (HttpContext http) => string.Join(",", http.User
                .FindAll(ClaimTypes.GroupSid).Select(c => c.Value).Order(StringComparer.Ordinal)))
            .RequireAuthorization(AvalonRoles.Player);
        app.MapGet("/anonymous", (HttpContext http) => http.User.Identity?.IsAuthenticated == true ? "user" : "anonymous")
            .AllowAnonymous();
        // Stands in for the launcher sign-in endpoints (#591): the same named policy, no dependencies.
        app.MapGet(ClientAuthLimitedPath, () => "ok").RequireRateLimiting(ClientAuthRateLimiting.Policy);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }

    public static Account MakeAccount(AccountAccessLevel level = AccountAccessLevel.Player,
        AccountStatus status = AccountStatus.Active) => new()
        {
            Id = new AccountId(AccountIdValue),
            Username = "CALLER",
            Email = "caller@avalon.monster",
            Salt = [1],
            Verifier = [2],
            JoinDate = DateTime.UtcNow,
            AccessLevel = level,
            Status = status,
        };

    /// <summary>
    /// What the account lookups return for exactly <see cref="AccountIdValue"/>, and nothing else: the repository
    /// authentication reads (#794) and the account service the controllers read.
    /// </summary>
    public void AccountNowIs(Account? account)
    {
        Accounts.FindByIdAsync(Arg.Is<AccountId>(id => id.Value == AccountIdValue), Arg.Any<CancellationToken>())
            .Returns(account);
        AccountRepository.FindByIdAsync(Arg.Is<AccountId>(id => id.Value == AccountIdValue), Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(account);
    }

    /// <summary>An argument matching the hash a personal access token is looked up by.</summary>
    public static byte[] TokenHash(string token)
    {
        byte[] expected = PersonalAccessTokens.Hash(token);
        return Arg.Is<byte[]>(hash => hash.SequenceEqual(expected));
    }

    /// <summary>What the personal access token lookup returns for <paramref name="token"/>.</summary>
    public void PatIs(string token, PersonalAccessToken? pat) =>
        PatRepository.FindByHashAsync(TokenHash(token), Arg.Any<CancellationToken>()).Returns(pat);

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

    /// <summary>
    /// Answers every request once routing has chosen its endpoint: 204 with <see cref="MatchedRouteHeader"/> naming the
    /// endpoint's route when one matched, 404 when none did. Nothing after it runs.
    /// </summary>
    private sealed class RouteProbe : IApiService
    {
        public static readonly RouteProbe Service = new();

        public string Name => "route-probe";

        public Assembly ControllerAssembly => typeof(RouteProbe).Assembly;

        public ApiServiceNeeds Needs { get; } = new(false, WorldDatabaseParts.None, AuthSchemaRole.Reader, false);

        public void AddServices(WebApplicationBuilder builder)
        {
        }

        public void UseBeforeAuthentication(IApplicationBuilder app) => app.Use((HttpContext context, RequestDelegate _) =>
        {
            Endpoint? endpoint = context.GetEndpoint();
            if (endpoint is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            context.Response.StatusCode = StatusCodes.Status204NoContent;
            context.Response.Headers[MatchedRouteHeader] = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? endpoint.DisplayName;
            return Task.CompletedTask;
        });
    }
}

/// <summary>How <see cref="ApiTestHost"/> builds its host.</summary>
public sealed class ApiTestHostOptions
{
    /// <summary>The cache the login policy counts on; a plain substitute when not given.</summary>
    public IReplicatedCache? Cache { get; init; }

    /// <summary>Runs after the host's own registrations and its substitutes, so a test can put a real service in place of a substitute.</summary>
    public Action<IServiceCollection>? Configure { get; init; }

    /// <summary>Settings over <see cref="ApiTestHost.Settings"/>.</summary>
    public IReadOnlyDictionary<string, string?>? Settings { get; init; }

    /// <summary>
    /// Answer every request once routing has chosen its endpoint, with 204 and <see cref="ApiTestHost.MatchedRouteHeader"/>
    /// naming its route, or 404 when none matched, so a test sees which endpoint a request reaches without running it.
    /// </summary>
    public bool ProbeRoutes { get; init; }
}
