using System.Globalization;
using System.Security.Claims;
using Avalon.Api.Services;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Avalon.Api.Authentication.Jwt;

/// <summary>
/// Runs once the bearer handler has validated a JWT's signature, issuer, audience and lifetime:
/// reloads the account the token names and applies <see cref="AccountAccessCheck"/>, the same
/// rule a personal access token gets. A refused account fails authentication (401); an admitted
/// one has its role claims replaced by the token's roles masked by the account's current ones.
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

        AccountAccessCheck.Remember(context.HttpContext, account);

        var source = principal.Identity as ClaimsIdentity;
        var claims = principal.Claims.Where(c => !string.Equals(c.Type, ClaimTypes.GroupSid, StringComparison.Ordinal))
            .Concat(AccountAccessCheck.RoleClaims(roles));
        var identity = new ClaimsIdentity(claims, source?.AuthenticationType ?? context.Scheme.Name,
            source?.NameClaimType ?? ClaimTypes.Name, ClaimTypes.GroupSid);
        context.Principal = new ClaimsPrincipal(identity);
    }
}
