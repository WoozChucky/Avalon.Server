using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.Extensions.Logging;
using OtpNet;
using StackExchange.Redis;

namespace Avalon.Infrastructure.Services;

public interface IMFAHashService
{
    Task<string> GenerateHashAsync(Account account);
    Task<AccountId?> GetAccountIdAsync(string hash);

    /// <summary>
    /// The credentials version of the row whose password issued the account's live MFA hash
    /// (#495), or -1 when there is no live hash or it carries none. -1 is never a version, so a
    /// caller comparing it with the account's refuses.
    /// </summary>
    Task<int> GetCredentialsVersionAsync(AccountId accountId);
    Task CleanupHash(string hash);

    /// <summary>
    /// Counts one code attempt against the account's live MFA hash and returns the count so far,
    /// or -1 when the account has no live hash.
    /// </summary>
    Task<long> RecordAttemptAsync(AccountId accountId);

    /// <summary>
    /// Spends the hash: deletes it, and returns true only to the caller whose delete removed its
    /// reverse key. Redis tells exactly one caller that, so of two verifies racing on one hash,
    /// only one may go on (#478, as #450 does for world keys).
    /// </summary>
    Task<bool> TryConsumeAsync(string hash, AccountId accountId);

    /// <summary>
    /// Gives back one code attempt counted by <see cref="RecordAttemptAsync"/>, for a code that was
    /// right but replayed (#478 re-review). Does nothing when the account has no live hash.
    /// </summary>
    Task GiveBackAttemptAsync(AccountId accountId);
}

public class MFAHashService : IMFAHashService
{
    private readonly IReplicatedCache _cache;
    private readonly TimeSpan _expiry = TimeSpan.FromMinutes(2);
    private readonly ILogger<MFAHashService> _logger;

    public MFAHashService(ILoggerFactory loggerFactory, IReplicatedCache cache)
    {
        _logger = loggerFactory.CreateLogger<MFAHashService>();
        _cache = cache;
    }

    public async Task<string> GenerateHashAsync(Account account)
    {
        // Check for existing hash
        RedisValue existingHash = await _cache.Database.HashGetAsync(CacheKeys.AccountMfa(account.Id), "hash");
        if (existingHash.HasValue)
        {
            RedisValue expiry = await _cache.Database.HashGetAsync(CacheKeys.AccountMfa(account.Id), "expiry");
            // Reused only while it was issued at this row's credentials version (#495): a hash left
            // by a login with the old password must not be handed to a login with the new one.
            int existingVersion = await GetCredentialsVersionAsync(account.Id);
            if (DateTime.TryParse(expiry, out DateTime expiryDate) && expiryDate > DateTime.UtcNow
                && existingVersion == account.CredentialsVersion)
            {
                _logger.LogDebug("Returning existing hash");
                return existingHash!;
            }

            _logger.LogDebug("Removing expired hash");
            await CleanupHash(existingHash!);
        }

        byte[]? secretKey = KeyGeneration.GenerateRandomKey(20);
        string? hash = Base32Encoding.ToString(secretKey);

        // StackExchange.Redis transactions: the per-op Tasks returned by
        // ITransaction methods DO NOT complete until ExecuteAsync runs, so we
        // must not `await` them individually — that was the hang. Queue the
        // ops (discard their Tasks), include the reverse-hash SET for proper
        // atomicity, then await ExecuteAsync once.
        ITransaction transaction = _cache.Database.CreateTransaction();
        _ = transaction.HashSetAsync(CacheKeys.AccountMfa(account.Id),
            new[]
            {
                new HashEntry("hash", hash),
                new HashEntry("expiry", DateTime.UtcNow.Add(_expiry).ToString("O")),
                new HashEntry("accountId", account.Id.Value.ToString()),
                new HashEntry(CredentialsVersionField,
                    account.CredentialsVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
            });
        _ = transaction.KeyExpireAsync(CacheKeys.AccountMfa(account.Id), _expiry);
        _ = transaction.StringSetAsync(
            CacheKeys.MfaReverseHash(hash),
            account.Id!.Value.ToString(),
            _expiry);

        bool committed = await transaction.ExecuteAsync();
        if (!committed)
        {
            _logger.LogWarning("MFA hash transaction failed for account {AccountId}", account.Id);
        }
        return hash;
    }

    public async Task<AccountId?> GetAccountIdAsync(string hash)
    {
        var accountIdStr = await _cache.GetAsync(CacheKeys.MfaReverseHash(hash));
        if (accountIdStr == null) return null;
        return new AccountId(long.Parse(accountIdStr));
    }

    /// <summary>The hash field holding the issuing row's credentials version (#495).</summary>
    public const string CredentialsVersionField = "cver";

    public async Task<int> GetCredentialsVersionAsync(AccountId accountId)
    {
        RedisValue value = await _cache.Database.HashGetAsync(CacheKeys.AccountMfa(accountId.Value),
            CredentialsVersionField);
        return value.HasValue && int.TryParse(value.ToString(), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out int version)
            ? version
            : -1;
    }

    public Task<long> RecordAttemptAsync(AccountId accountId) =>
        _cache.HashIncrementIfExistsAsync(CacheKeys.AccountMfa(accountId.Value), "attempts");

    public Task GiveBackAttemptAsync(AccountId accountId) =>
        _cache.HashDecrementFloorIfExistsAsync(CacheKeys.AccountMfa(accountId.Value), "attempts");

    public async Task<bool> TryConsumeAsync(string hash, AccountId accountId)
    {
        // The DEL spends the hash, not the GET before it: two callers can both have read the
        // reverse key, but only one is told it deleted it.
        if (!await _cache.RemoveAsync(CacheKeys.MfaReverseHash(hash)))
            return false;

        await _cache.RemoveAsync(CacheKeys.AccountMfa(accountId.Value));
        return true;
    }

    public async Task CleanupHash(string hash)
    {
        var accountId = await GetAccountIdAsync(hash);
        if (accountId != null)
        {
            await _cache.RemoveAsync(CacheKeys.AccountMfa((long)accountId));
        }
        await _cache.RemoveAsync(CacheKeys.MfaReverseHash(hash));
    }
}
