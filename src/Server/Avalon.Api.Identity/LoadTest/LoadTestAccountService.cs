using System.Security.Cryptography;
using System.Text;
using Avalon.Api.Contract;
using Avalon.Api.Hosting.Exceptions;
using Avalon.Api.Hosting.Worlds;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;

namespace Avalon.Api.Identity.LoadTest;

/// <summary>Creates and deletes runs of load-test bot accounts (<c>admin/load-test/accounts</c>).</summary>
public interface ILoadTestAccounts
{
    /// <summary>
    /// Creates a run of <see cref="CreateLoadTestRunRequest.Count"/> accounts for <paramref name="admin"/>, who has
    /// already proved their current password. 400 (<see cref="BusinessException"/>) for a count, bot password or run
    /// id outside the rules; 409 (<see cref="LoadTestConflictException"/>) past <see cref="LoadTestOptions.MaxAccounts"/>
    /// or for a run id already used.
    /// </summary>
    Task<LoadTestRunCreated> CreateAsync(AccountId admin, CreateLoadTestRunRequest request, CancellationToken ct);

    /// <summary>
    /// Deletes run <paramref name="runId"/>'s accounts, or every load-test account when <paramref name="all"/> is true,
    /// for <paramref name="admin"/>, who has already proved their current password, with their characters in every
    /// configured world. Exactly one of the two: neither, both, or an empty or blank run id is a 400
    /// (<see cref="BusinessException"/>), as is a run id outside the rules. 409 (<see cref="LoadTestConflictException"/>)
    /// with nothing deleted while one of them plays or a world's database is unavailable; 409 too when a bot enters a
    /// game during the delete, after the run's characters were removed but before its accounts were: stop the bots
    /// and repeat the request to finish it.
    /// </summary>
    Task<LoadTestRunDeleted> DeleteAsync(AccountId admin, string? runId, bool all, CancellationToken ct);
}

/// <summary>
/// A run is <c>count</c> accounts named <c>LT</c> + the run id + a fixed-width letter index (<c>LTABCAAAAAAA</c>,
/// <c>LTABCAAAAAAB</c>, ...; 12 letters, so a name is also a valid character name), with no email, <c>Player | PTR</c>,
/// and one shared password, hashed once for the run. Each holds an Avalon stored grant for the base game in the
/// configured store environment under the reference <c>loadtest:&lt;runId&gt;:&lt;accountId&gt;</c>, which is what
/// marks an account as a load-test account: the cap counts accounts by it and a run id is used once it appears. The
/// accounts and their grants are inserted in one auth transaction. Admin-created, so no per-source budget is spent.
/// </summary>
public sealed partial class LoadTestAccountService(IDbTransactionRunner<AuthDbContext> auth, IOptions<LoadTestOptions> options,
    IOptions<StoreAuthenticationConfiguration> store, IWorldDatabases configuredWorlds, IWorldDbContextFactory worldContexts,
    IReplicatedCache cache, TimeProvider clock, ILogger<LoadTestAccountService> logger)
    : ILoadTestAccounts
{
    /// <summary>The most accounts one request creates.</summary>
    public const int MaxRunSize = 1000;

    /// <summary>The bot password's minimum length, measured trimmed, as at registration.</summary>
    public const int MinPasswordLength = 8;

    public const int RunIdLength = 3;
    public const int IndexLength = 7;
    public const string NamePrefix = "LT";

    /// <summary>What every load-test license reference starts with.</summary>
    public const string ReferencePrefix = "loadtest:";

    /// <summary>How many random run ids are tried before giving up.</summary>
    private const int RunIdAttempts = 32;

    /// <summary>The license reference of <paramref name="accountId"/> in run <paramref name="runId"/>.</summary>
    public static string Reference(string runId, long accountId) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{ReferencePrefix}{runId}:{accountId}");

    /// <summary>What the license references of run <paramref name="runId"/> start with.</summary>
    public static string RunReferencePrefix(string runId) => $"{ReferencePrefix}{runId}:";

    /// <summary>The username of the account at <paramref name="index"/> (from 0) in run <paramref name="runId"/>.</summary>
    public static string AccountName(string runId, int index)
    {
        Span<char> letters = stackalloc char[IndexLength];
        for (int i = IndexLength - 1; i >= 0; i--)
        {
            letters[i] = (char)('A' + (index % 26));
            index /= 26;
        }

        return string.Concat(NamePrefix, runId, letters);
    }

    public async Task<LoadTestRunCreated> CreateAsync(AccountId admin, CreateLoadTestRunRequest request, CancellationToken ct)
    {
        int count = request.Count;
        if (count is < 1 or > MaxRunSize)
            throw new BusinessException($"A run has from 1 to {MaxRunSize} accounts.");

        // Measured and hashed trimmed, as a registration's password is.
        string password = (request.Password ?? "").Trim();
        if (password.Length < MinPasswordLength)
        {
            throw new BusinessException(
                $"The bot password must be at least {MinPasswordLength} characters long, not counting leading or trailing spaces.");
        }

        string? requested = null;
        if (request.RunId is not null)
        {
            if (request.RunId.Length != RunIdLength || !request.RunId.All(char.IsAsciiLetter))
                throw new BusinessException($"A run id is {RunIdLength} ASCII letters.");
            requested = request.RunId.ToUpperInvariant();
        }

        // Once for the whole run, before the transaction opens: BCrypt is slow by design.
        string salt = BCrypt.Net.BCrypt.GenerateSalt();
        byte[] saltBytes = Encoding.UTF8.GetBytes(salt);
        byte[] verifier = Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword(password, salt));

        DateTime now = clock.GetUtcNow().UtcDateTime;
        string environment = store.Value.Environment;
        int cap = options.Value.MaxAccounts;
        string? runId = requested;

        LoadTestRunCreated created;
        try
        {
            created = await auth.ExecuteAsync(async (db, token) =>
            {
                int existing = await CountLoadTestAccountsAsync(db, token);
                if (existing + count > cap)
                {
                    throw new LoadTestConflictException(
                        $"There are {existing} load-test accounts; {count} more would pass the cap of {cap}.");
                }

                if (requested is not null)
                {
                    if (await RunIdUsedAsync(db, requested, count, token))
                        throw new LoadTestConflictException($"Run id {requested} is already used.");
                }
                else
                {
                    runId = await NewRunIdAsync(db, count, token);
                }

                string[] names = new string[count];
                var accounts = new List<Account>(count);
                for (int i = 0; i < count; i++)
                {
                    names[i] = AccountName(runId!, i);
                    accounts.Add(new Account
                    {
                        Username = names[i],
                        Email = null,
                        Salt = saltBytes,
                        Verifier = verifier,
                        JoinDate = now,
                        LastLogin = now,
                        AccessLevel = AccountAccessLevel.Player | AccountAccessLevel.PTR,
                    });
                }

                await AccountRepository.InsertManyAsync(db, accounts, token);
                await GameLicenseRepository.RecordNewGrantsAsync(db, accounts.ConvertAll(account => new GameLicense
                {
                    Id = Guid.NewGuid(),
                    AccountId = account.Id,
                    Provider = StoreProviders.Avalon,
                    Environment = environment,
                    Product = StoreAuthenticationConfiguration.Product,
                    ProviderProductId = StoreAuthenticationConfiguration.NativeProviderProduct,
                    LicenseReference = Reference(runId!, account.Id.Value),
                    AuthorityKind = LicenseAuthorityKind.StoredGrant,
                    GrantedAt = now,
                }), token);

                return new LoadTestRunCreated(runId!, names);
            }, ct);
        }
        catch (DbUpdateException ex) when (runId is not null)
        {
            // Another run, or a registration, took one of these names between the check and the insert.
            if (await auth.ExecuteAsync((db, token) => RunIdUsedAsync(db, runId, count, token), ct))
                throw new LoadTestConflictException($"Run id {runId} was taken while it was being created.", ex);
            throw;
        }

        logger.LogInformation("Admin {AdminId} created load-test run {RunId} of {Count} accounts",
            admin.Value, created.RunId, created.Accounts.Count);
        return created;
    }

    /// <summary>The accounts that hold a load-test license.</summary>
    private static Task<int> CountLoadTestAccountsAsync(AuthDbContext db, CancellationToken ct) =>
        db.GameLicenses.Where(license => license.Provider == StoreProviders.Avalon &&
                EF.Functions.Like(license.LicenseReference, ReferencePrefix + "%"))
            .Select(license => license.AccountId).Distinct().CountAsync(ct);

    /// <summary>
    /// Whether run <paramref name="runId"/> is used: a license names it, or an account already holds one of the
    /// <paramref name="count"/> names it would create (a player may have registered one).
    /// </summary>
    private static async Task<bool> RunIdUsedAsync(AuthDbContext db, string runId, int count, CancellationToken ct)
    {
        string prefix = RunReferencePrefix(runId);
        if (await db.GameLicenses.AnyAsync(license => license.Provider == StoreProviders.Avalon &&
                EF.Functions.Like(license.LicenseReference, prefix + "%"), ct))
        {
            return true;
        }

        string[] names = new string[count];
        for (int i = 0; i < count; i++)
            names[i] = AccountName(runId, i);
        return await db.Accounts.AnyAsync(account => names.Contains(account.Username), ct);
    }

    private static async Task<string> NewRunIdAsync(AuthDbContext db, int count, CancellationToken ct)
    {
        for (int attempt = 0; attempt < RunIdAttempts; attempt++)
        {
            string runId = string.Create(RunIdLength, 0, static (letters, _) =>
            {
                for (int i = 0; i < letters.Length; i++)
                    letters[i] = (char)('A' + RandomNumberGenerator.GetInt32(26));
            });
            if (!await RunIdUsedAsync(db, runId, count, ct))
                return runId;
        }

        throw new LoadTestConflictException("No unused run id was found; name one.");
    }
}
