using System.Net;
using Avalon.Api.Authentication;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

public class BaseController : ControllerBase
{
    protected Account? Account => (Account?)HttpContext.Items[nameof(Account)];
    protected IPAddress IpAddress => HttpContext.Connection.RemoteIpAddress ?? IPAddress.None;
    protected CancellationToken CancellationToken => HttpContext.RequestAborted;

    // Existence-hiding: Player-only callers cannot distinguish "doesn't exist"
    // from "exists but not mine". GameMaster+ sees the real Forbid.
    protected IActionResult NotFoundOrForbid() =>
        User.HasRoleAtLeast(AvalonRoles.GameMaster) ? Forbid() : NotFound();
}
