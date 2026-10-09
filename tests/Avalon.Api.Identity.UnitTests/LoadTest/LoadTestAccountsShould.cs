using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Avalon.Api.Contract;
using Avalon.Api.Identity.Services;
using Avalon.Api.Testing;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;

namespace Avalon.Api.Identity.UnitTests.LoadTest;

/// <summary>
/// <c>admin/load-test/accounts</c>: an admin creates a run of bot accounts the load-test client signs in with. Each bot
/// is a <c>Player | PTR</c> account holding the base game, named so a run's accounts are told apart from players'.
/// Runs the real controller and service over HTTP, against a real relational schema.
/// </summary>
public sealed class LoadTestAccountsShould : IDisposable
{
    private static readonly string s_adminPassword = TestPasswords.Valid;
    private static readonly string s_botPassword = TestPasswords.Other;

    private readonly SqliteAuthDatabase _database = new();
    private readonly AccountRepository _accounts;

    public LoadTestAccountsShould()
    {
        _accounts = new AccountRepository(_database);
    }

    public void Dispose() => _database.Dispose();

    /// <summary>The admin <see cref="ApiTestHost"/> authenticates, id 7, held by the real database.</summary>
    private Task<Account> AdminAsync()
    {
        string salt = BCrypt.Net.BCrypt.GenerateSalt(4);
        return _accounts.CreateAsync(new Account
        {
            Id = new AccountId(ApiTestHost.AccountIdValue),
            Username = "STAFFER",
            Email = "staffer@avalon.monster",
            Salt = Encoding.UTF8.GetBytes(salt),
            Verifier = Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword(s_adminPassword, salt)),
            JoinDate = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow,
            AccessLevel = AccountAccessLevel.Player | AccountAccessLevel.Admin,
        });
    }

    private async Task<List<string>> UsernamesAsync()
    {
        await using AuthDbContext db = _database.CreateDbContext();
        return await db.Accounts.Select(a => a.Username).ToListAsync();
    }

    /// <summary>Identity alone, enabled, with the real account service and repositories over the test database.</summary>
    private async Task<ApiTestHost> HostAsync()
    {
        ApiTestHost host = await ApiTestHost.StartAsync([IdentityApi.Service], new ApiTestHostOptions
        {
            Settings = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Application:LoadTest:Enabled"] = "true",
                // The store settings a deployment has; the grants are recorded in its environment.
                ["Application:StoreAuthentication:SteamPublisherKey"] = "private-test-publisher-key",
                ["Application:StoreAuthentication:SteamAppId"] =
                    StoreAuthenticationTestData.SteamAppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            Configure = services =>
            {
                services.AddSingleton<IDbContextFactory<AuthDbContext>>(_database);
                services.AddSingleton<IAccountRepository>(_accounts);
                services.AddScoped<IAccountService, AccountService>();
            },
        });
        host.Refresh.IssueAsync(Arg.Any<AccountId>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new RefreshIssueResult("refresh", DateTime.UtcNow.AddDays(1), Guid.NewGuid()));
        return host;
    }

    [Fact]
    public async Task Create_a_run_of_licensed_ptr_accounts()
    {
        Account admin = await AdminAsync();
        List<string> before = await UsernamesAsync();
        await using ApiTestHost host = await HostAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/admin/load-test/accounts")
        {
            Content = JsonContent.Create(new { count = 3, password = s_botPassword, currentPassword = s_adminPassword }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiTestHost.Mint(admin));
        using HttpResponseMessage response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        LoadTestRunCreated? run = await response.Content.ReadFromJsonAsync<LoadTestRunCreated>();
        Assert.NotNull(run);
        Assert.Matches("^[A-Z]{3}$", run.RunId);
        string[] names = [$"LT{run.RunId}AAAAAAA", $"LT{run.RunId}AAAAAAB", $"LT{run.RunId}AAAAAAC"];
        Assert.Equal(names, run.Accounts);

        await using AuthDbContext db = _database.CreateDbContext();
        List<Account> bots = await db.Accounts.AsNoTracking().Where(a => !before.Contains(a.Username)).ToListAsync();
        Assert.Equal(names, bots.Select(a => a.Username).Order(StringComparer.Ordinal));
        Assert.All(bots, bot =>
        {
            Assert.Equal(AccountAccessLevel.Player | AccountAccessLevel.PTR, bot.AccessLevel);
            Assert.Null(bot.Email);
            Assert.Equal(0, bot.CredentialsVersion);
        });
        // One hash for the whole run: BCrypt salts every hash afresh, so equal verifiers were hashed once.
        Assert.Single(bots.Select(bot => Convert.ToBase64String(bot.Verifier)).Distinct(StringComparer.Ordinal));

        string environment = host.Services.GetRequiredService<IOptions<StoreAuthenticationConfiguration>>().Value.Environment;
        List<GameLicense> licenses = await db.GameLicenses.AsNoTracking().ToListAsync();
        Assert.Equal(
            bots.Select(bot => (bot.Id.Value, $"loadtest:{run.RunId}:{bot.Id.Value}")).Order(),
            licenses.Select(license => (license.AccountId.Value, license.LicenseReference)).Order());
        DateTime now = DateTime.UtcNow;
        Assert.All(licenses, license =>
        {
            Assert.Equal(StoreProviders.Avalon, license.Provider);
            Assert.Equal(StoreAuthenticationConfiguration.NativeProviderProduct, license.ProviderProductId);
            Assert.Equal(LicenseAuthorityKind.StoredGrant, license.AuthorityKind);
            Assert.True(license.Authorizes(license.AccountId, StoreAuthenticationConfiguration.Product, environment, now));
        });

        // A bot signs in through the normal login with the run's password.
        using HttpResponseMessage login = await host.Client.PostAsJsonAsync("/account/authenticate",
            new { username = names[1], password = s_botPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        AuthenticateResponse? session = await login.Content.ReadFromJsonAsync<AuthenticateResponse>();
        Assert.Equal(AuthenticationResponseStatus.Success, session?.Status);
        Assert.False(string.IsNullOrEmpty(session?.Token));
    }
}
