namespace Avalon.Infrastructure.GameAuth;

/// <summary>An atomic write set. Expected null means absent; value null deletes. The expiry is authoritative backend UTC.</summary>
public sealed record GameAuthMutation(string Key, string? Expected, string? Value, DateTime ExpiresAt);

public interface IGameContextStore
{
    Task<string?> ReadAsync(string key, CancellationToken cancellationToken);
    Task<bool> CompareExchangeAsync(IReadOnlyList<GameAuthMutation> mutations, CancellationToken cancellationToken);
}
