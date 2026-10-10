using Avalon.Api.Identity.Config;
using Avalon.Api.Identity.Services;
using Avalon.Api.Identity.UnitTests.GameAuth;
using Avalon.Api.Testing;
using Avalon.Common.GameAuth;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Configuration;
using Avalon.Infrastructure;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.GameTickets;
using Avalon.Infrastructure.Services;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.Services;

/// <summary>
/// A ban against a real database, through the real transaction runner. All three writes run on the
/// one context the runner opens; a ban that revoked no credentials would leave the banned account
/// a live session for as long as its tokens last.
/// </summary>
public class AccountStatusChangeShould : IDisposable
{
    private readonly SqliteAuthDatabase _database = new();

    [Fact]
    public async Task Revoke_every_credential_the_account_holds()
    {
        AccountId actor = new(1);
        AccountRepository accounts = new(_database);

        Account account = await accounts.CreateAsync(new Account
        {
            Username = "BANME",
            Email = "banme@avalon.monster",
            Salt = [1],
            Verifier = [2],
            SessionKey = [],
            LastIp = "127.0.0.1",
            LastAttemptIp = string.Empty,
            MuteBy = string.Empty,
            MuteReason = string.Empty,
            JoinDate = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow,
        });

        await new RefreshTokenRepository(_database).CreateAsync(new RefreshToken
        {
            AccountId = account.Id,
            FamilyId = Guid.NewGuid(),
            Index = 0,
            Hash = [9, 9, 9],
            Revoked = false,
            Usages = 0,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        });

        await new PersonalAccessTokenRepository(_database).CreateAsync(new PersonalAccessToken
        {
            AccountId = account.Id,
            Name = "cli",
            TokenHash = [7, 7, 7],
            TokenPrefix = "avp_1234",
            Roles = AccountAccessLevel.Player,
            CreatedAt = DateTime.UtcNow,
        });

        AccountService service = Service(accounts);

        await service.UpdateStatusAsync(account.Id, Contract.AccountStatus.Banned, "spam", actor);

        await using AuthDbContext context = _database.CreateDbContext();
        Assert.Equal(AccountStatus.Banned, (await context.Accounts.SingleAsync(a => a.Id == account.Id)).Status);
        Assert.True(await context.RefreshTokens.Where(t => t.AccountId == account.Id).AllAsync(t => t.Revoked));
        Assert.True(await context.PersonalAccessTokens.Where(t => t.AccountId == account.Id)
            .AllAsync(t => t.RevokedAt != null && t.RevokedBy == actor));
    }

    /// <summary>
    /// #882: a ban left the account's game contexts in Redis untouched, refused only while the account read as banned,
    /// so lifting the ban within a context's twelve hours brought every one of them back. The ban moves the session
    /// epoch in its own transaction, and every context bound to the old epoch stays void for good.
    /// </summary>
    [Fact]
    public async Task Void_the_game_contexts_of_a_banned_account_even_after_the_ban_is_lifted()
    {
        AccountRepository accounts = new(_database);
        Account account = await accounts.CreateAsync(new Account
        {
            Username = "PLAYER",
            Email = "player@avalon.monster",
            Salt = [1],
            Verifier = [2],
            SessionKey = [],
            LastIp = "127.0.0.1",
            LastAttemptIp = string.Empty,
            MuteBy = string.Empty,
            MuteReason = string.Empty,
            JoinDate = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow,
        });
        var store = new AtomicAuthStore();
        var crypto = new GameAuthCryptography(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray());
        IOptions<StoreAuthenticationConfiguration> options = Options.Create(new StoreAuthenticationConfiguration { SteamAppId = 2499460 });
        var family = Guid.NewGuid();
        IRefreshTokenRepository families = Substitute.For<IRefreshTokenRepository>();
        families.IsLiveLauncherFamilyAsync(account.Id, family, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        var licenses = new MemoryGameLicenses();
        licenses.Rows.Add(new GameLicense
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Provider = "avalon",
            Product = StoreAuthenticationConfiguration.Product,
            Environment = "production",
            ProviderProductId = "base",
            LicenseReference = "grant",
            AuthorityKind = LicenseAuthorityKind.StoredGrant,
            GrantedAt = DateTime.UtcNow,
        });
        GameAuthorizationService authorization = TestGameAuthorization.Create(store, new(store, crypto, options, TimeProvider.System), crypto,
            accounts, families, Substitute.For<IExternalIdentityRepository>(), Substitute.For<ILicenseObservationRepository>(),
            Substitute.For<ISteamProofVerifier>(), Substitute.For<ISteamOwnershipClient>(), options, TimeProvider.System, gameLicenses: licenses);
        AuthAttemptReply attempt = (await authorization.CreateAttemptAsync("avalon", "1", Guid.NewGuid(), new string('A', 43), null, null, default))!;
        string ticket = GameAuthCryptography.NewToken();
        store.Seed(RedisGameTicketStore.Key(ticket), $"{account.Id.Value}|{family:D}|{account.CredentialsVersion}|{account.SessionEpoch}|production");
        GameAuthReply signedIn = await authorization.RedeemHandoffAsync(attempt.AttemptCredential, ticket, Guid.NewGuid(), default);
        Assert.NotNull(await authorization.GetContextAsync(signedIn.GameContextCredential!, true, default));
        AccountService service = Service(accounts);

        await service.UpdateStatusAsync(account.Id, Contract.AccountStatus.Banned, "spam", new AccountId(1));
        await service.UpdateStatusAsync(account.Id, Contract.AccountStatus.Active, "appeal", new AccountId(1));

        Assert.Null(await authorization.GetContextAsync(signedIn.GameContextCredential!, false, default));
        Assert.Equal(GameAuthErrors.ContextRevoked,
            (await authorization.RefreshAsync(signedIn.GameContextRefreshToken!, Guid.NewGuid(), default)).Error);
    }

    private AccountService Service(AccountRepository accounts) => new(
        NullLoggerFactory.Instance,
        accounts,
        Substitute.For<Avalon.Api.Identity.Authentication.Jwt.IJwtUtils>(),
        Substitute.For<IMFAHashService>(),
        new MfaSetupRepository(_database),
        new DeviceRepository(_database),
        Substitute.For<IReplicatedCache>(),
        Substitute.For<ISecureRandom>(),
        new DbTransactionRunner<AuthDbContext>(_database),
        new AuthenticationConfig(),
        TestLogin.Password(accounts, Substitute.For<IReplicatedCache>()),
        TestLogin.Reauthentication(accounts, Substitute.For<IReplicatedCache>()));

    public void Dispose() => _database.Dispose();
}
