using System.Security.Claims;
using System.Text.Encodings.Web;
using Avalon.Api.Services;
using Avalon.Common.Accounts;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Avalon.Api.Authentication.AV;

public class AvalonAuthenticationHandler : AuthenticationHandler<AvalonAuthenticationSchemeOptions>
{
    private readonly IPersonalAccessTokenService _pats;
    private readonly IAccountService _accounts;
    private const string Prefix = "avp_";
    private const int ExpectedLength = 47;

    public AvalonAuthenticationHandler(
        IOptionsMonitor<AvalonAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IPersonalAccessTokenService pats,
        IAccountService accounts)
        : base(options, logger, encoder)
    {
        _pats = pats;
        _accounts = accounts;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderNames.Authorization, out StringValues value))
            return AuthenticateResult.NoResult();

        string[] parts = value.ToString().Split(' ', 2);
        if (parts.Length != 2 || !string.Equals(parts[0], "Avalon", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        string token = parts[1];
        if (!token.StartsWith(Prefix, StringComparison.Ordinal) || token.Length != ExpectedLength)
            return AuthenticateResult.Fail("invalid token format");

        PersonalAccessToken? pat = await _pats.FindByRawTokenAsync(token, Context.RequestAborted);
        if (pat is null) return AuthenticateResult.Fail("invalid token");
        if (pat.RevokedAt is not null) return AuthenticateResult.Fail("token revoked");
        if (pat.ExpiresAt is { } exp && exp < DateTime.UtcNow) return AuthenticateResult.Fail("token expired");

        Account? account = await _accounts.FindByIdAsync(pat.AccountId, Context.RequestAborted);
        // A token never carries more than the account holds now: the mint-time cap in
        // PersonalAccessTokenService is not enough on its own, because the account can be
        // demoted afterwards (#451). The token keeps its narrower scope otherwise.
        if (!AccountAccessCheck.TryAdmit(account, pat.Roles, out AccountAccessLevel effectiveRoles, out string? refusal))
            return AuthenticateResult.Fail(refusal);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.Value.ToString()),
            new(ClaimTypes.Name, account.Username),
            // Account.Email is nullable and a claim's value is not: an account without an email throws here, as it
            // always has. The code standard changes no behaviour (#791).
#pragma warning disable CS8604
            new(ClaimTypes.Email, account.Email),
#pragma warning restore CS8604
            new("pat_id", pat.Id.Value.ToString()),
        };
        claims.AddRange(AccountAccessCheck.RoleClaims(effectiveRoles));
        AccountAccessCheck.Remember(Context, account);

        // Fire-and-forget write-coalesced last-used update — don't block the request.
        await _pats.TouchLastUsedAsync(pat.Id, CancellationToken.None);

        var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.NameIdentifier, ClaimTypes.GroupSid);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
