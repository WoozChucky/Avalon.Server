using System.Diagnostics.Metrics;
using System.Globalization;
using Avalon.Common.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Avalon.Infrastructure.GameAuth;

public interface IGameContextRevocations
{
    Task PublishAsync(AccountId accountId, Guid contextId);
}

/// <summary>Hints only. Durable context and lease checks remain authoritative if Redis loses a notice.</summary>
public sealed class GameContextRevocations(IReplicatedCache cache, ILogger<GameContextRevocations> logger) : IGameContextRevocations
{
    private static readonly Meter Meter = new("Avalon.GameAuthentication");
    private static readonly Counter<long> Notices = Meter.CreateCounter<long>("avalon.game_auth.revocation_notifications");
    private const int MaximumMessageCharacters = 19 + 1 + 32; // Int64 account, separator, GUID N.
    public const string Channel = "world:game-context:revoke";
    public async Task PublishAsync(AccountId accountId, Guid contextId)
    {
        try
        {
            await cache.PublishAsync(Channel, accountId.Value.ToString(CultureInfo.InvariantCulture) + "|" + contextId.ToString("N"));
            Notices.Add(1, new KeyValuePair<string, object?>("outcome", "published"));
        }
        catch (Exception error) { Notices.Add(1, new KeyValuePair<string, object?>("outcome", "failed")); logger.LogWarning(error, "Could not publish game context revalidation for account {AccountId}", accountId.Value); }
    }
    public static bool TryParse(string message, out AccountId accountId, out Guid contextId)
    {
        accountId = null!; contextId = default;
        if (message.Length > MaximumMessageCharacters) return false;
        string[] parts = message.Split('|');
        if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long id) || id <= 0 ||
            id.ToString(CultureInfo.InvariantCulture) != parts[0] || parts[1].Length != 32 || !Guid.TryParseExact(parts[1], "N", out contextId))
        {
            return false;
        }

        accountId = new AccountId(id); return true;
    }
}
