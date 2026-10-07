using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Avalon.Api.Hosting.Authentication.AV;

/// <summary>
/// The personal access token scheme (<c>Authorization: Avalon avp_...</c>): the token is looked up by its hash, must be
/// live, and its account must pass <see cref="AccountAccessCheck"/>. It reads <see cref="IPersonalAccessTokenRepository"/>
/// and <see cref="IAccountRepository"/>, which every API service has (#794); the service that mints tokens keeps the
/// rest of their life cycle.
/// </summary>
public class AvalonAuthenticationHandler : AuthenticationHandler<AvalonAuthenticationSchemeOptions>
{
    private readonly IPersonalAccessTokenRepository _pats;
    private readonly IAccountRepository _accounts;
    private readonly TimeProvider _time;
    private const string Prefix = "avp_";
    private const int ExpectedLength = 47;

    public AvalonAuthenticationHandler(
        IOptionsMonitor<AvalonAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IPersonalAccessTokenRepository pats,
        IAccountRepository accounts,
        TimeProvider time)
        : base(options, logger, encoder)
    {
        _pats = pats;
        _accounts = accounts;
        _time = time;
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

        PersonalAccessToken? pat = await _pats.FindByHashAsync(PersonalAccessTokens.Hash(token), Context.RequestAborted);
        if (pat is null) return AuthenticateResult.Fail("invalid token");
        if (pat.RevokedAt is not null) return AuthenticateResult.Fail("token revoked");
        if (pat.ExpiresAt is { } exp && exp < DateTime.UtcNow) return AuthenticateResult.Fail("token expired");

        Account? account = await _accounts.FindByIdAsync(pat.AccountId, track: false, Context.RequestAborted);
        // A token never carries more than the account holds now: the mint-time cap in
        // PersonalAccessTokenService is not enough on its own, because the account can be
        // demoted afterwards (#451). The token keeps its narrower scope otherwise.
        if (!AccountAccessCheck.TryAdmit(account, pat.Roles, out AccountAccessLevel effectiveRoles, out string? refusal))
            return AuthenticateResult.Fail(refusal);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.Value.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, account.Username),
        };
        // A Steam-only account has no email until it adds recovery credentials: its principal carries no email claim.
        if (account.Email is { } email)
            claims.Add(new Claim(ClaimTypes.Email, email));
        claims.Add(new Claim("pat_id", pat.Id.Value.ToString()));
        claims.AddRange(AccountAccessCheck.RoleClaims(effectiveRoles));
        AccountAccessCheck.Remember(Context, account);

        // Fire-and-forget write-coalesced last-used update — don't block the request.
        await _pats.UpdateLastUsedIfStaleAsync(pat.Id, _time.GetUtcNow().UtcDateTime, PersonalAccessTokens.LastUsedBucket,
            CancellationToken.None);

        var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.NameIdentifier, ClaimTypes.GroupSid);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
