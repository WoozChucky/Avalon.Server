using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

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
        var userAgent = Request.Headers.UserAgent.ToString();
        await _notificationService.RegisterSubscriptionAsync(Account!, userAgent, request, CancellationToken);
        return Ok();
    }
}
