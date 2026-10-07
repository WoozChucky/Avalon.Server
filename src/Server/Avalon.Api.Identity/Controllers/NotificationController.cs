using Avalon.Api.Contract;
using Avalon.Api.Hosting.Controllers;
using Avalon.Api.Identity.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Identity.Controllers;

[Authorize]
[ApiController]
[Route("notification")]
public class NotificationController : BaseController
{

    private readonly INotificationService _notificationService;

    public NotificationController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterSubscriptionAsync([FromBody] PushSubscriptionRequest request)
    {
        // get user agent to register along with subscription
        string userAgent = Request.Headers.UserAgent.ToString();
        await _notificationService.RegisterSubscriptionAsync(Account!, userAgent, request, CancellationToken);
        return Ok();
    }
}
