using System.Security.Cryptography;
using System.Text;
using Avalon.Common.ValueObjects;
using Avalon.Infrastructure;

namespace Avalon.Api.Commerce;

public interface ICheckoutBudget
{
    Task<bool> TryTakeAsync(string environment, AccountId account, string source, Guid operation, CancellationToken ct);
}

/// <summary>Counts each durable operation once; shared source/account limits and marker are atomic.</summary>
public sealed class CheckoutBudget(IReplicatedCache cache) : ICheckoutBudget
{
    internal const int AccountLimit = 5;
    internal const int SourceLimit = 20;
    internal const int WindowSeconds = 3600;
    internal const int OperationMarkerSeconds = 86400;
    internal const string Script = """
        if redis.call('EXISTS', KEYS[1]) == 1 then return 1 end
        local account = tonumber(redis.call('GET', KEYS[2]) or '0')
        local source = tonumber(redis.call('GET', KEYS[3]) or '0')
        if account >= tonumber(ARGV[1]) or source >= tonumber(ARGV[2]) then return 0 end
        if redis.call('INCR', KEYS[2]) == 1 then redis.call('EXPIRE', KEYS[2], ARGV[3]) end
        if redis.call('INCR', KEYS[3]) == 1 then redis.call('EXPIRE', KEYS[3], ARGV[3]) end
        redis.call('SET', KEYS[1], '1', 'EX', ARGV[4])
        return 1
        """;
    public async Task<bool> TryTakeAsync(string environment, AccountId account, string source, Guid operation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var prefix = $"commerce:{{{environment}}}:checkout:";
        var sourceDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        var result = await cache.Database.ScriptEvaluateAsync(Script,
            [prefix + "operation:" + operation.ToString("N"), prefix + "account:" + account.Value, prefix + "source:" + sourceDigest],
            [AccountLimit, SourceLimit, WindowSeconds, OperationMarkerSeconds]).WaitAsync(ct);
        return (long)result == 1;
    }
}
