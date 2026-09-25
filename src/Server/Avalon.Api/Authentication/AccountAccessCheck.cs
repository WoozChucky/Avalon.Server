using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Avalon.Common.Accounts;
using Avalon.Domain.Auth;

namespace Avalon.Api.Authentication;

/// <summary>
/// The per-request account check both credential kinds go through: a JWT
/// (<see cref="Jwt.JwtAccountRevalidation"/>) and a personal access token
/// (<see cref="AV.AvalonAuthenticationHandler"/>). A credential says who the caller was and
/// what they held when it was issued; the account says what they hold now, and only the
/// account is believed. A missing or non-Active account is refused, and the roles a request
/// carries are the credential's roles masked by the account's current
/// <see cref="Account.AccessLevel"/>, so a demotion or a ban takes effect on the next request
/// rather than when the credential runs out (#451, #480).
/// </summary>
public static class AccountAccessCheck
{
    private static readonly AccountAccessLevel[] Flags =
        Enum.GetValues<AccountAccessLevel>().Where(flag => flag != 0).ToArray();

    /// <summary>
    /// Admits <paramref name="account"/> for a credential holding <paramref name="credentialRoles"/>:
    /// false, with the reason, when the account is missing or not Active; otherwise true, with the
    /// roles the request may carry.
    /// </summary>
    public static bool TryAdmit([NotNullWhen(true)] Account? account, AccountAccessLevel credentialRoles,
        out AccountAccessLevel roles, [NotNullWhen(false)] out string? refusal)
    {
        roles = 0;
        if (account is null)
        {
            refusal = "account not found";
            return false;
        }

        if (account.Status != AccountStatus.Active)
        {
            refusal = "account inactive";
            return false;
        }

        refusal = null;
        // AccountAccessLevel is [Flags]: a mask, never an ordinal comparison.
        roles = credentialRoles & account.AccessLevel;
        return true;
    }

    /// <summary>One <see cref="ClaimTypes.GroupSid"/> claim per flag set in <paramref name="roles"/>.</summary>
    public static IEnumerable<Claim> RoleClaims(AccountAccessLevel roles) =>
        Flags.Where(flag => (roles & flag) == flag)
            .Select(flag => new Claim(ClaimTypes.GroupSid, flag.ToString()));

    /// <summary>
    /// The roles named by <paramref name="principal"/>'s <see cref="ClaimTypes.GroupSid"/> claims.
    /// Only an exact flag name counts: a number or a comma list is not a role.
    /// </summary>
    public static AccountAccessLevel RolesOf(ClaimsPrincipal principal)
    {
        AccountAccessLevel roles = 0;
        foreach (Claim claim in principal.FindAll(ClaimTypes.GroupSid))
            foreach (AccountAccessLevel flag in Flags)
                if (string.Equals(claim.Value, flag.ToString(), StringComparison.Ordinal))
                    roles |= flag;
        return roles;
    }
}
