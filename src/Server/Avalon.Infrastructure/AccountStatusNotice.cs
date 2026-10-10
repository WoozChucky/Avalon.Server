using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Avalon.Network.Packets.Generic;

namespace Avalon.Infrastructure;

/// <summary>
/// The message on <see cref="CacheKeys.WorldAccountsStatusChannel"/> (#882): the account id and the status that ends its
/// sessions, <c>{accountId}|BANNED</c> or <c>{accountId}|DEACTIVATED</c>, and what a closed connection is told.
/// </summary>
public static class AccountStatusNotice
{
    public const string Banned = "BANNED";
    public const string Deactivated = "DEACTIVATED";
    public const string BannedMessage = "Your account has been banned.";
    public const string DeactivatedMessage = "Your account has been deactivated.";

    /// <summary>An Int64 account, the separator and the longer status.</summary>
    private const int MaximumMessageCharacters = 19 + 1 + 11;

    /// <summary>The notice for <paramref name="status"/>, or null for a status that ends no session.</summary>
    public static string? Format(AccountId accountId, AccountStatus status) => status switch
    {
        AccountStatus.Banned => accountId.Value.ToString(CultureInfo.InvariantCulture) + "|" + Banned,
        AccountStatus.Deactivated => accountId.Value.ToString(CultureInfo.InvariantCulture) + "|" + Deactivated,
        _ => null,
    };

    /// <summary>
    /// Reads a notice: the account it names, the reason its connections close with and what they are told. Anything
    /// else (another shape, a status it does not know, an id that is not canonical) is refused.
    /// </summary>
    public static bool TryParse(string message, [NotNullWhen(true)] out AccountId? accountId, out DisconnectReason reason,
        [NotNullWhen(true)] out string? text)
    {
        accountId = null; reason = default; text = null;
        if (message.Length > MaximumMessageCharacters) return false;
        int separator = message.IndexOf('|', StringComparison.Ordinal);
        if (separator <= 0) return false;
        ReadOnlySpan<char> id = message.AsSpan(0, separator);
        if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out long value) || value <= 0 ||
            !id.SequenceEqual(value.ToString(CultureInfo.InvariantCulture)))
        {
            return false;
        }

        switch (message.AsSpan(separator + 1))
        {
            case Banned:
                (reason, text) = (DisconnectReason.Banned, BannedMessage);
                break;
            case Deactivated:
                (reason, text) = (DisconnectReason.Deactivated, DeactivatedMessage);
                break;
            default:
                return false;
        }

        accountId = new AccountId(value);
        return true;
    }
}
