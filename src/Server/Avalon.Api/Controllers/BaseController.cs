using System.Net;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

public class BaseController : ControllerBase
{
    protected Account? Account => (Account?)HttpContext.Items[nameof(Account)];
    protected IPAddress IpAddress => HttpContext.Connection.RemoteIpAddress ?? IPAddress.None;
    protected CancellationToken CancellationToken => HttpContext.RequestAborted;
}
