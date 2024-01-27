using System.Collections.Concurrent;
using Avalon.Domain.Auth;
using OtpNet;

namespace Avalon.Api.Services;

public interface IMFAHashService : IWorkerService
{
    Task<string> GenerateHashAsync(Account account);
    Task<Account?> GetAccountAsync(string hash);
    Task CleanupHash(string hash);
}

public class MFAHashService : IMFAHashService
{
    private readonly ILogger<MFAHashService> _logger;
    private readonly IDictionary<string, (Account, string, DateTime)> _hashes;
    
    public MFAHashService(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<MFAHashService>();
        _hashes = new ConcurrentDictionary<string, (Account, string, DateTime)>();
    }
    
    public async Task<string> GenerateHashAsync(Account account)
    {
        foreach (var tuple in _hashes)
        {
            if (tuple.Value.Item1.Id == account.Id && tuple.Value.Item3.AddMinutes(2) > DateTime.UtcNow)
            {
                _logger.LogDebug("Returning existing hash");
                return tuple.Value.Item2;
            }

            if (tuple.Value.Item1.Id == account.Id && tuple.Value.Item3.AddMinutes(2) < DateTime.UtcNow)
            {
                _logger.LogDebug("Removing expired hash");
                await CleanupHash(tuple.Value.Item2);
            }
        }
        
        var secretKey = KeyGeneration.GenerateRandomKey(20);
        var hash = Base32Encoding.ToString(secretKey);
        
        _hashes.Add(hash, (account, hash, DateTime.UtcNow));
        return hash;
    }
    
    public async Task<Account?> GetAccountAsync(string hash)
    {
        if (_hashes.TryGetValue(hash, out var tuple))
        {
            if (tuple.Item3.AddMinutes(2) < DateTime.UtcNow)
            {
                await CleanupHash(hash);
                return null;
            }
            return tuple.Item1;
        }
        return null;
    }
    
    public async Task StartWorker(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var hash in _hashes)
            {
                if (hash.Value.Item3.AddMinutes(2) < DateTime.UtcNow)
                {
                    _logger.LogDebug("Removing expired hash");
                    await CleanupHash(hash.Key);
                }
            }
            await Task.Delay(1000, cancellationToken);
        }
    }

    public async Task CleanupHash(string hash)
    {
        if (_hashes.ContainsKey(hash))
        {
            _hashes.Remove(hash);
        }
    }
}
