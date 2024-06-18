using Avalon.Domain.Auth;
using Microsoft.Extensions.Logging;
using OtpNet;
using StackExchange.Redis;

namespace Avalon.Infrastructure.Services;

public interface IMFAHashService
{
    Task<string> GenerateHashAsync(Account account);
    Task<int?> GetAccountIdAsync(string hash);
    Task CleanupHash(string hash);
}

public class MFAHashService : IMFAHashService
{
    private readonly IReplicatedCache _cache;
    private readonly ILogger<MFAHashService> _logger;
    private readonly TimeSpan _expiry = TimeSpan.FromMinutes(2);
    
    public MFAHashService(ILoggerFactory loggerFactory, IReplicatedCache cache)
    {
        _logger = loggerFactory.CreateLogger<MFAHashService>();
        _cache = cache;
    }
    
    public async Task<string> GenerateHashAsync(Account account)
    {
        // Check for existing hash
        var existingHash = await _cache.Database.HashGetAsync($"auth:account:{account.Id}:mfa", "hash");
        if (existingHash.HasValue)
        {
            var expiry = await _cache.Database.HashGetAsync($"auth:account:{account.Id}:mfa", "expiry");
            if (DateTime.TryParse(expiry, out var expiryDate) && expiryDate > DateTime.UtcNow)
            {
                _logger.LogDebug("Returning existing hash");
                return existingHash!;
            }
            
            _logger.LogDebug("Removing expired hash");
            await CleanupHash(existingHash!);
        }
        
        var secretKey = KeyGeneration.GenerateRandomKey(20);
        var hash = Base32Encoding.ToString(secretKey);
        
        var transaction = _cache.Database.CreateTransaction();
        await transaction.HashSetAsync($"auth:account:{account.Id}:mfa", new []
        {
            new HashEntry("hash", hash),
            new HashEntry("expiry", DateTime.UtcNow.Add(_expiry).ToString("O")),
            new HashEntry("accountId", account.Id!.Value.ToString())
        });
        await transaction.ExecuteAsync();
        return hash;
    }
    
    public async Task<int?> GetAccountIdAsync(string hash)
    {
        var keys = await _cache.Database.ExecuteAsync("keys", "auth:account:*:mfa");
        foreach (var (key, _) in keys.ToDictionary())
        {
            var value = await _cache.Database.HashGetAsync(key, "hash");
            if (value != hash) continue;
            var accountId = await _cache.Database.HashGetAsync(key, "accountId");
            return int.Parse(accountId!);
        }
        return null;
    }

    public async Task CleanupHash(string hash)
    {
        var keys = await _cache.Database.ExecuteAsync("keys", $"account:*:mfa");
        foreach (var (key, _) in keys.ToDictionary())
        {
            var value = await _cache.Database.HashGetAsync(key, "hash");
            if (value != hash) continue;
            await _cache.Database.KeyDeleteAsync(key);
        }
    }
}
