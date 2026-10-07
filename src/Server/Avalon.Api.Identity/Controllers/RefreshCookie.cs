using Avalon.Api.Identity.Config;

namespace Avalon.Api.Identity.Controllers;

/// <summary>The refresh token's cookie, set by every endpoint that issues one: login, MFA verify and refresh.</summary>
internal static class RefreshCookie
{
    public static void Set(HttpResponse response, string rawToken, DateTime expiresAt, AuthenticationConfig config)
    {
        response.Cookies.Append(
            config.RefreshCookieName,
            rawToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = config.RefreshCookiePath,
                Expires = expiresAt,
            });
    }
}
