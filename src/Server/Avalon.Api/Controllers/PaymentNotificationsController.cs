using Avalon.Api.Commerce;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

[AllowAnonymous]
[ApiController]
[Route("payments/notifications")]
[RequestSizeLimit(CommercePolicy.MaximumNotificationBytes)]
public sealed class PaymentNotificationsController(IPaymentNotificationService notifications) : ControllerBase
{
    [HttpPost("{provider}", Name = "ReceivePaymentNotification")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Receive([FromRoute] string provider, CancellationToken ct)
    {
        using var body = new MemoryStream();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = await Request.Body.ReadAsync(buffer, ct)) != 0)
        {
            if (body.Length + read > CommercePolicy.MaximumNotificationBytes) return BadRequest();
            await body.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        var headers = Request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        NotificationAcceptance result = await notifications.AcceptAsync(provider, body.ToArray(), headers, ct);
        return result switch { NotificationAcceptance.Accepted => Ok(), NotificationAcceptance.Disabled => NotFound(), _ => BadRequest() };
    }
}
