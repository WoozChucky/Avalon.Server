using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Avalon.Api.Authentication.AV;

public class AvalonAuthenticationHandler : AuthenticationHandler<AvalonAuthenticationSchemeOptions>
{
    [Obsolete("ISystemClock is obsolete, use TimeProvider on AuthenticationSchemeOptions instead.")]
    public AvalonAuthenticationHandler(IOptionsMonitor<AvalonAuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
        : base(options, logger, encoder, clock) { }

    public AvalonAuthenticationHandler(IOptionsMonitor<AvalonAuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderNames.Authorization, out var value))
        {
            return await Task.FromResult(AuthenticateResult.Fail("Header Not Found."));
        }

        if (Context.User.Identity?.IsAuthenticated ?? false) return AuthenticateResult.NoResult();

        var header = value.ToString();
        var segments = header.Split(' ');
        if (segments.Length != 2) return AuthenticateResult.NoResult();
        if (segments[0].ToLowerInvariant() != "avalon") return AuthenticateResult.NoResult();
        var token = segments[1];
        if (string.IsNullOrWhiteSpace(token)) return AuthenticateResult.Fail("Invalid bearer token");

        // TODO: validate token

        var claims = new[] {
            new Claim("test", "value"),
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);

    }
}
