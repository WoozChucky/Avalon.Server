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
    Task CleanupHash(string hash);
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
            if (DateTime.TryParse(expiry, out DateTime expiryDate) && expiryDate > DateTime.UtcNow)
            {
                _logger.LogDebug("Returning existing hash");
                return existingHash!;
            }

            _logger.LogDebug("Removing expired hash");
            await CleanupHash(existingHash!);
        }

        byte[]? secretKey = KeyGeneration.GenerateRandomKey(20);
        string? hash = Base32Encoding.ToString(secretKey);

        ITransaction transaction = _cache.Database.CreateTransaction();
        await transaction.HashSetAsync(CacheKeys.AccountMfa(account.Id),
            new[]
            {
                new HashEntry("hash", hash),
                new HashEntry("expiry", DateTime.UtcNow.Add(_expiry).ToString("O")),
                new HashEntry("accountId", account.Id.Value.ToString())
            });
        await transaction.ExecuteAsync();
        await _cache.SetAsync(CacheKeys.MfaReverseHash(hash), account.Id!.Value.ToString(), _expiry);
        return hash;
    }

    public async Task<AccountId?> GetAccountIdAsync(string hash)
    {
        var accountIdStr = await _cache.GetAsync(CacheKeys.MfaReverseHash(hash));
        if (accountIdStr == null) return null;
        return new AccountId(long.Parse(accountIdStr));
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
