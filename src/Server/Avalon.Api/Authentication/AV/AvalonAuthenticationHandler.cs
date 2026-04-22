using System.Security.Claims;
using System.Text.Encodings.Web;
using Avalon.Api.Services;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
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
        if (!Request.Headers.TryGetValue(HeaderNames.Authorization, out var value))
            return AuthenticateResult.NoResult();

        var parts = value.ToString().Split(' ', 2);
        if (parts.Length != 2 || !string.Equals(parts[0], "Avalon", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = parts[1];
        if (!token.StartsWith(Prefix, StringComparison.Ordinal) || token.Length != ExpectedLength)
            return AuthenticateResult.Fail("invalid token format");

        var pat = await _pats.FindByRawTokenAsync(token, Context.RequestAborted);
        if (pat is null) return AuthenticateResult.Fail("invalid token");
        if (pat.RevokedAt is not null) return AuthenticateResult.Fail("token revoked");
        if (pat.ExpiresAt is { } exp && exp < DateTime.UtcNow) return AuthenticateResult.Fail("token expired");

        var account = await _accounts.FindByIdAsync(pat.AccountId, Context.RequestAborted);
        if (account is null) return AuthenticateResult.Fail("account not found");
        if (account.Status != AccountStatus.Active) return AuthenticateResult.Fail("account inactive");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.Value.ToString()),
            new(ClaimTypes.Name, account.Username),
            new(ClaimTypes.Email, account.Email),
            new("pat_id", pat.Id.Value.ToString()),
        };
        foreach (AccountAccessLevel flag in Enum.GetValues<AccountAccessLevel>())
            if (flag != 0 && pat.Roles.HasFlag(flag))
                claims.Add(new Claim(ClaimTypes.GroupSid, flag.ToString()));

        // Fire-and-forget write-coalesced last-used update — don't block the request.
        _ = _pats.TouchLastUsedAsync(pat.Id, CancellationToken.None);

        var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.NameIdentifier, ClaimTypes.GroupSid);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
