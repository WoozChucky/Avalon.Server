using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Avalon.Api.Services;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Avalon.Api.Authentication.Jwt;

/// <summary>
/// Runs once the bearer handler has validated a JWT's signature, issuer, audience and lifetime:
/// reloads the account the token names and applies <see cref="AccountAccessCheck"/>, the same
/// rule a personal access token gets. A refused account fails authentication (401); an admitted
/// one has its role claims replaced by the token's roles masked by the account's current ones.
/// A token issued before the account's credentials last changed (#495) is refused too.
/// </summary>
public static class JwtAccountRevalidation
{
    public static async Task OnTokenValidated(TokenValidatedContext context)
    {
        ClaimsPrincipal? principal = context.Principal;
        string? subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal is null || !long.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out long id))
        {
            context.Fail("token names no account");
            return;
        }

        IAccountService accounts = context.HttpContext.RequestServices.GetRequiredService<IAccountService>();
        Account? account = await accounts.FindByIdAsync(new AccountId(id), context.HttpContext.RequestAborted);

        if (!AccountAccessCheck.TryAdmit(account, AccountAccessCheck.RolesOf(principal),
                out AccountAccessLevel roles, out string? refusal))
        {
            context.Fail(refusal);
            return;
        }

        // A password change, an MFA reset or an admin's MFA removal ends every access token issued
        // before it (#495), rather than leaving it its lifetime.
        if (IssuedBeforeCredentialsChanged(IssuedAt(context.SecurityToken), account))
        {
            context.Fail("credentials changed");
            return;
        }

        AccountAccessCheck.Remember(context.HttpContext, account);

        var source = principal.Identity as ClaimsIdentity;
        var claims = principal.Claims.Where(c => !string.Equals(c.Type, ClaimTypes.GroupSid, StringComparison.Ordinal))
            .Concat(AccountAccessCheck.RoleClaims(roles));
        var identity = new ClaimsIdentity(claims, source?.AuthenticationType ?? context.Scheme.Name,
            source?.NameClaimType ?? ClaimTypes.Name, ClaimTypes.GroupSid);
        context.Principal = new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Whether a token issued at <paramref name="issuedAt"/> predates the account's last
    /// credentials change. <c>iat</c> has whole seconds, so the change is compared at the second:
    /// a token issued in the same second as the change is accepted (a login straight after a
    /// change must work). A token with no <c>iat</c> counts as issued at the earliest instant.
    /// </summary>
    internal static bool IssuedBeforeCredentialsChanged(DateTime issuedAt, Account account)
    {
        if (account.CredentialsChangedAt is not { } changedAt)
            return false;

        DateTime changedAtSecond = new(changedAt.Ticks - changedAt.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        return issuedAt < changedAtSecond;
    }

    private static DateTime IssuedAt(Microsoft.IdentityModel.Tokens.SecurityToken? token) => token switch
    {
        JsonWebToken jwt => jwt.IssuedAt,
        JwtSecurityToken jwt => jwt.IssuedAt,
        _ => DateTime.MinValue,
    };
}
