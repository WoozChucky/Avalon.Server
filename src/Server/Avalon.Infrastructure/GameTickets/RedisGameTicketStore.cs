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
            $"{grant.AccountId.Value}|{grant.FamilyId:D}|{grant.CredentialsVersion}");
        if (!await cache.SetNxAsync(Key(ticket), value, Lifetime))
            throw new InvalidOperationException("Could not issue game ticket");
        return ticket;
    }

    public async Task<GameTicketGrant?> RedeemAsync(string ticket, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ticket.Length != 43 || ticket.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            return null;

        string? value = await cache.TakeAsync(Key(ticket));
        if (value is null) return null;
        string[] parts = value.Split('|');
        if (parts.Length != 3
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long accountId)
            || !Guid.TryParseExact(parts[1], "D", out Guid familyId)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int credentialsVersion))
            return null;
        return new GameTicketGrant(new AccountId(accountId), familyId, credentialsVersion);
    }

    private static string Key(string ticket)
    {
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(ticket));
        return CacheKeys.GameTicket(Convert.ToHexStringLower(hash));
    }
}
