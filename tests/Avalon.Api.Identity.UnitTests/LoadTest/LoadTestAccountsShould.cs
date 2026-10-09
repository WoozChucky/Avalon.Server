using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Avalon.Api.Contract;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Identity.Services;
using Avalon.Api.Testing;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character;
using Avalon.Domain.Auth;
using Avalon.Domain.Commerce;
using Avalon.Domain.World;
using Avalon.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using CharacterRow = Avalon.Domain.Characters.Character;

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

    /// <summary>Two worlds' characters databases, as identity holds every configured world's.</summary>
    private readonly SqliteWorlds _worlds = new(1, 2);

    public LoadTestAccountsShould()
    {
        _accounts = new AccountRepository(_database);
    }

    public void Dispose()
    {
        _worlds.Dispose();
        _database.Dispose();
    }

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
                var configured = new WorldDatabases([
                    new ConfiguredWorld(new WorldId(1), null, "Host=c1"), new ConfiguredWorld(new WorldId(2), null, "Host=c2"),
                ]);
                services.AddSingleton(configured);
                services.AddSingleton<IWorldDatabases>(configured);
                services.AddSingleton<IWorldDbContextFactory>(_worlds);
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

    [Fact]
    public async Task Delete_a_run_and_only_load_test_accounts()
    {
        Account admin = await AdminAsync();
        await using ApiTestHost host = await HostAsync();
        string token = ApiTestHost.Mint(admin);
        DateTime now = DateTime.UtcNow;

        (string first, List<Account> bots) = await CreateRunAsync(host, token);
        (string second, List<Account> others) = await CreateRunAsync(host, token);

        // Players who hold one of the first run's licenses and something a run never gives, one per reason; and a player
        // who only shares the name pattern.
        const AccountAccessLevel Bot = AccountAccessLevel.Player | AccountAccessLevel.PTR;
        var reasons = new (string Suffix, AccountAccessLevel Access, string? Email, Action<AuthDbContext, Account> Holds)[]
        {
            ("ZZZZZZA", Bot | AccountAccessLevel.Admin, null, (_, _) => { }),
            ("ZZZZZZB", Bot, "kept@avalon.monster", (_, _) => { }),
            ("ZZZZZZC", Bot, null, (db, account) => db.ExternalIdentities.Add(new ExternalIdentity
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                Provider = StoreProviders.Steam,
                ProviderSubject = "76561198000000001",
                LinkedAt = now,
            })),
            ("ZZZZZZD", Bot, null, (db, account) => db.PurchaseOrders.Add(new PurchaseOrder
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                OriginalPurchaserAccountId = account.Id,
                Product = StoreAuthenticationConfiguration.Product,
                OfferId = "base",
                AmountMinor = 1999,
                Currency = "eur",
                Provider = "stripe",
                ProviderAccountId = "acct_test",
                PaymentEnvironment = "test",
                LicenseEnvironment = "dev",
                CreatedAt = now,
            })),
            ("ZZZZZZE", Bot, null, (db, account) => db.GameLicenses.Add(License(account, $"order:{Guid.NewGuid():N}", now))),
        };
        var kept = new List<Account>();
        foreach ((string suffix, AccountAccessLevel access, string? email, Action<AuthDbContext, Account> holds) in reasons)
        {
            Account player = await PlayerAsync($"LT{first}{suffix}", access, email);
            await using AuthDbContext db = _database.CreateDbContext();
            db.GameLicenses.Add(License(player, $"loadtest:{first}:{player.Id.Value}", now));
            holds(db, player);
            await db.SaveChangesAsync();
            kept.Add(player);
        }

        Account named = await PlayerAsync($"LT{first}ZZZZZZY", Bot, null);
        List<Account> survivors = [.. others, .. kept, named];
        string[] keptNames = [.. kept.Select(a => a.Username)];

        await using (AuthDbContext db = _database.CreateDbContext())
        {
            // What an admission and a hold leave on a bot's license; both point at it without cascading.
            GameLicense botLicense = await db.GameLicenses.SingleAsync(l => l.AccountId == bots[0].Id);
            db.LicenseObservations.Add(new LicenseObservation
            {
                Id = Guid.NewGuid(),
                LicenseId = botLicense.Id,
                AuthorityRevision = 1,
                AccountId = bots[0].Id,
                Provider = StoreProviders.Avalon,
                ProviderSubject = "",
                Environment = botLicense.Environment,
                Product = botLicense.Product,
                ProviderProductId = botLicense.ProviderProductId,
                OwnsProduct = true,
                ObservedAt = now,
                AuthorizedUntil = now.AddMinutes(5),
                PolicyVersion = 1,
            });
            db.LicenseHolds.Add(new LicenseHold
            {
                Id = Guid.NewGuid(),
                LicenseId = botLicense.Id,
                CauseKind = "dispute",
                CauseReference = "d1",
                StartedAt = now,
            });
            await db.SaveChangesAsync();
        }

        // Everyone plays in both worlds; each character holds an item.
        uint nextCharacter = 1;
        foreach (ushort world in s_worldIds)
        {
            await using CharacterDbContext db = _worlds.CreateCharacters(new WorldId(world));
            foreach (Account owner in bots.Concat(survivors))
            {
                var id = new CharacterId(nextCharacter++);
                db.Characters.Add(new CharacterRow
                {
                    Id = id,
                    AccountId = owner.Id,
                    Name = $"C{world}{owner.Username}",
                    CreationDate = now,
                });
                db.ItemInstances.Add(new ItemInstance
                {
                    Id = new ItemInstanceId(Guid.NewGuid()),
                    CharacterId = id,
                    TemplateId = new ItemTemplateId(1),
                    Count = 1,
                    UpdatedAt = now,
                });
            }

            // Saving them writes each owner's gameplay fence.
            await db.SaveChangesAsync();
        }

        object body = new { currentPassword = s_adminPassword };
        string deleteFirst = $"/admin/load-test/accounts?run={first}";

        // Every run is asked for by name only: no scope, a blank run, or both is refused before the password is
        // checked (a wrong one here), whatever variable was left unset.
        foreach (string query in new[] { "", "?run=", "?run=%20%20%20", $"?run={second}&all=true", "?all=false" })
        {
            using HttpResponseMessage refused = await SendAsync(host, token, HttpMethod.Delete,
                "/admin/load-test/accounts" + query, new { currentPassword = "not-the-password" });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        // A bot still in a game session holds the whole run back, and so does a bot's character online in any world:
        // nothing is deleted, in no database.
        await using (AuthDbContext db = _database.CreateDbContext())
        {
            db.GameSessions.Add(new GameSession
            {
                AccountId = bots[0].Id,
                GameSessionId = Guid.NewGuid(),
                GameContextId = Guid.NewGuid(),
                FencingToken = 1,
                ServerId = "world-1",
                WorldId = 1,
                Environment = "dev",
                State = GameSessionState.Active,
                CreatedAt = now,
                LeaseUntil = now.AddMinutes(1),
                LicenseUntil = now.AddMinutes(5),
            });
            await db.SaveChangesAsync();
        }

        using (HttpResponseMessage refused = await SendAsync(host, token, HttpMethod.Delete, deleteFirst, body))
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        await using (AuthDbContext db = _database.CreateDbContext())
            await db.GameSessions.ExecuteUpdateAsync(u => u.SetProperty(s => s.State, GameSessionState.Ended));
        await using (CharacterDbContext db = _worlds.CreateCharacters(new WorldId(2)))
            await db.Characters.Where(c => c.AccountId == bots[1].Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.Online, true));

        using (HttpResponseMessage refused = await SendAsync(host, token, HttpMethod.Delete, deleteFirst, body))
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        await AssertPlayingAsync([.. bots, .. survivors]);
        Assert.Subset((await UsernamesAsync()).ToHashSet(StringComparer.Ordinal),
            bots.Concat(survivors).Select(a => a.Username).ToHashSet(StringComparer.Ordinal));

        await using (CharacterDbContext db = _worlds.CreateCharacters(new WorldId(2)))
            await db.Characters.ExecuteUpdateAsync(u => u.SetProperty(c => c.Online, false));

        // The first run goes; the second run and every player stay, the players named as skipped.
        using (HttpResponseMessage response = await SendAsync(host, token, HttpMethod.Delete, deleteFirst, body))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            LoadTestRunDeleted? deleted = await response.Content.ReadFromJsonAsync<LoadTestRunDeleted>();
            Assert.Equal(2, deleted?.Deleted);
            Assert.Equal(keptNames, deleted?.Skipped);
        }

        await AssertPlayingAsync(survivors);
        await using (AuthDbContext db = _database.CreateDbContext())
        {
            List<string> left = await db.Accounts.Select(a => a.Username).ToListAsync();
            Assert.DoesNotContain(left, name => bots.Any(b => b.Username == name));
            Assert.All(survivors, survivor => Assert.Contains(survivor.Username, left));
            List<long> licensed = (await db.GameLicenses.Select(l => l.AccountId).ToListAsync()).ConvertAll(id => id.Value);
            // Each second-run bot and each kept player keeps its license; the other-license player keeps both.
            Assert.Equal(others.Concat(kept).Append(kept[^1]).Select(a => a.Id.Value).Order(), licensed.Order());
            Assert.Empty(await db.LicenseObservations.ToListAsync());
            Assert.Empty(await db.LicenseHolds.ToListAsync());
            Assert.Empty(await db.GameSessions.ToListAsync());
        }

        // Anything still holding a deleted bot is told to let go; nobody else is.
        await host.Cache.Received(bots.Count).PublishAsync(CacheKeys.WorldAccountsDisconnectChannel, Arg.Any<string>());
        foreach (Account deletedBot in bots)
        {
            await host.Cache.Received(1).PublishAsync(CacheKeys.WorldAccountsDisconnectChannel,
                deletedBot.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // Nothing of the first run is left to delete; the players are still skipped.
        using (HttpResponseMessage again = await SendAsync(host, token, HttpMethod.Delete, deleteFirst, body))
        {
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            LoadTestRunDeleted? none = await again.Content.ReadFromJsonAsync<LoadTestRunDeleted>();
            Assert.Equal(0, none?.Deleted);
            Assert.Equal(keptNames, none?.Skipped);
        }

        // Every run, asked for by name: the second run goes too, and the players still stay.
        using (HttpResponseMessage everything = await SendAsync(host, token, HttpMethod.Delete,
                   "/admin/load-test/accounts?all=true", body))
        {
            Assert.Equal(HttpStatusCode.OK, everything.StatusCode);
            LoadTestRunDeleted? deleted = await everything.Content.ReadFromJsonAsync<LoadTestRunDeleted>();
            Assert.Equal(2, deleted?.Deleted);
            Assert.Equal(keptNames, deleted?.Skipped);
        }

        await AssertPlayingAsync([.. kept, named]);
        List<string> remaining = await UsernamesAsync();
        Assert.DoesNotContain(remaining, name => others.Any(b => b.Username == name));
        Assert.All(kept.Append(named), player => Assert.Contains(player.Username, remaining));
    }

    private static readonly ushort[] s_worldIds = [1, 2];

    /// <summary>A run of 2 created through the endpoint: its id and its accounts.</summary>
    private async Task<(string RunId, List<Account> Bots)> CreateRunAsync(ApiTestHost host, string token)
    {
        using HttpResponseMessage created = await SendAsync(host, token, HttpMethod.Post, "/admin/load-test/accounts",
            new { count = 2, password = s_botPassword, currentPassword = s_adminPassword });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        LoadTestRunCreated run = (await created.Content.ReadFromJsonAsync<LoadTestRunCreated>())!;
        await using AuthDbContext db = _database.CreateDbContext();
        List<Account> bots = await db.Accounts.AsNoTracking().Where(a => run.Accounts.Contains(a.Username))
            .OrderBy(a => a.Id).ToListAsync();
        return (run.RunId, bots);
    }

    /// <summary>In both worlds, exactly these accounts have characters, items and gameplay fences.</summary>
    private async Task AssertPlayingAsync(IReadOnlyList<Account> accounts)
    {
        long[] expected = [.. accounts.Select(a => a.Id.Value).Order()];
        foreach (ushort world in s_worldIds)
        {
            await using CharacterDbContext db = _worlds.CreateCharacters(new WorldId(world));
            List<long> owners = await db.Characters.Select(c => c.AccountId.Value).ToListAsync();
            Assert.Equal(expected, owners.Order());
            // Each character's item goes with it.
            Assert.Equal(expected.Length, await db.ItemInstances.CountAsync());
            Assert.Equal(expected, (await db.AccountGameplayFences.Select(f => f.AccountId.Value).ToListAsync()).Order());
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(ApiTestHost host, string token, HttpMethod method, string path,
        object body)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await host.Client.SendAsync(request);
    }

    private Task<Account> PlayerAsync(string username, AccountAccessLevel access, string? email) => _accounts.CreateAsync(
        new Account
        {
            Username = username,
            Email = email,
            Salt = [1],
            Verifier = [2],
            JoinDate = DateTime.UtcNow,
            AccessLevel = access,
        });

    private static GameLicense License(Account account, string reference, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        AccountId = account.Id,
        Provider = StoreProviders.Avalon,
        Environment = "dev",
        Product = StoreAuthenticationConfiguration.Product,
        ProviderProductId = StoreAuthenticationConfiguration.NativeProviderProduct,
        LicenseReference = reference,
        AuthorityKind = LicenseAuthorityKind.StoredGrant,
        GrantedAt = now,
    };
}
