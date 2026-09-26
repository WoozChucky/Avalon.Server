using System.Net;
using Avalon.Api.Authentication;
using Avalon.Api.Config;
using Avalon.Api.Exceptions;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

public class BaseController : ControllerBase
{
    protected Account? Account => (Account?)HttpContext.Items[nameof(Account)];
    protected IPAddress IpAddress => HttpContext.Connection.RemoteIpAddress ?? IPAddress.None;

    /// <summary>
    /// The caller's address as the login policy's source (#478 review). A caller with none (a
    /// transport with no IP peer) is refused with 400: <see cref="IpAddress"/>'s
    /// <c>IPAddress.None</c> would put every such caller in one source budget, so failures by any
    /// of them would refuse them all, and a key per request would give them no source budget.
    /// </summary>
    protected IPAddress SourceAddress => HttpContext.Connection.RemoteIpAddress
        ?? throw new BusinessException("The caller's address is unknown, so this request cannot be accepted.");
    protected CancellationToken CancellationToken => HttpContext.RequestAborted;

    // Existence-hiding: Player-only callers cannot distinguish "doesn't exist"
    // from "exists but not mine". GameMaster+ sees the real Forbid.
    protected IActionResult NotFoundOrForbid() =>
        User.HasRoleAtLeast(AvalonRoles.GameMaster) ? Forbid() : NotFound();

    protected void SetRefreshCookie(string rawToken, DateTime expiresAt, AuthenticationConfig config)
    {
        Response.Cookies.Append(
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
