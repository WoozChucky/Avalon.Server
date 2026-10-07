using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Avalon.Common.ValueObjects;

namespace Avalon.Infrastructure.GameTickets;

public sealed class RedisGameTicketStore(IReplicatedCache cache) : IGameTicketStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    public async Task<string> IssueAsync(GameTicketGrant grant, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        string ticket = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        CryptographicOperations.ZeroMemory(bytes);
        string value = string.Create(CultureInfo.InvariantCulture,
            $"{grant.AccountId.Value}|{grant.FamilyId:D}|{grant.CredentialsVersion}|{grant.SessionEpoch}|{grant.Environment}");
        if (!await cache.SetNxAsync(Key(ticket), value, Lifetime)) throw new InvalidOperationException("Could not issue game ticket");
        return ticket;
    }

    public async Task<GameTicketGrant?> RedeemAsync(string ticket, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!GameAuth.GameAuthCryptography.IsToken(ticket)) return null;
        string? value = await cache.TakeAsync(Key(ticket));
        return TryParseValue(value, false, out GameTicketGrant? grant) ? grant : null;
    }

    /// <summary>Legacy TCP accepts the prior encoding until coordinated cutover; the game-context API requires explicit scope.</summary>
    public static bool TryParseValue(string? value, bool requireScope, out GameTicketGrant? grant)
    {
        grant = null;
        if (value is null || value.Length > 256) return false;
        string[] parts = value.Split('|');
        if ((parts.Length != 5 && (requireScope || parts.Length != 3)) ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long accountId) || accountId <= 0 ||
            !Guid.TryParseExact(parts[1], "D", out Guid family) || family == Guid.Empty ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int credentialsVersion))
        {
            return false;
        }

        long epoch = 0;
        string environment = "production";
        if (parts.Length == 5 && (!long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out epoch) ||
            (parts[4] != "production" && parts[4] != "development")))
        {
            return false;
        }

        if (parts.Length == 5) environment = parts[4];
        grant = new(new AccountId(accountId), family, credentialsVersion, epoch, environment);
        return true;
    }

    public static string Key(string ticket) => CacheKeys.GameTicket(Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(ticket))));
}
