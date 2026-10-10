using Avalon.Common.GameAuth;
using Avalon.Domain.Auth;

namespace Avalon.Infrastructure.GameAuth;

/// <summary>
/// What a game route tells a caller about an account it may not play on (#882). Asked only once the caller has proved
/// who they are and the account's credentials version matches that proof, so nothing is disclosed before it. The
/// password lock is not a standing: it guards the password steps only, never an identity proven another way.
/// </summary>
public static class GameAccountStanding
{
    /// <summary>The error naming why <paramref name="account"/> may not play, or null when its status lets it.</summary>
    public static string? Refusal(Account account) => account.Status switch
    {
        AccountStatus.Banned => GameAuthErrors.AccountBanned,
        AccountStatus.Deactivated => GameAuthErrors.AccountDeactivated,
        _ when account.GameplayConsolidationId is not null => GameAuthErrors.AccountConsolidating,
        _ => null,
    };
}
