using System.Security.Cryptography;
using System.Text.Json;
using Avalon.Api.Contract;
using Avalon.Api.Identity.Authentication;
using Avalon.Common.GameAuth;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace Avalon.Api.Identity.Controllers;

[ApiController, Route("internal/game")]
[Authorize(AuthenticationSchemes = GameServerAuthHandler.Scheme, Policy = GameServerAuthHandler.Scheme)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(GameAuthPolicy.MaximumControlBodyBytes)]
public sealed partial class InternalGameAdmissionController(ILogger<InternalGameAdmissionController> logger,
    JoinTicketStore tickets) : ControllerBase
{
    [HttpPost("join-tickets/redeem", Name = "RedeemGameJoinTicket")]
    [ProducesResponseType(typeof(JoinRedemptionReceipt), StatusCodes.Status200OK)]
    public async Task<IActionResult> Redeem(JoinRedemptionRequest request, CancellationToken cancellationToken)
    {
        string? serverId = User.FindFirst(GameServerAuthHandler.ServerIdClaim)?.Value;
        if (!Request.IsHttps || serverId is null) return Unauthorized(JoinRedemptionReceipt.Failure(GameAuthErrors.WorkloadAuthenticationRequired));
        try
        {
            JoinRedemptionReceipt receipt = await tickets.RedeemAsync(request.JoinTicket, serverId,
                request.ConnectionId, request.RedemptionId, cancellationToken);
            return receipt.Error switch
            {
                null => Ok(receipt),
                GameAuthErrors.InProgress or GameAuthErrors.SessionConflict => Conflict(receipt),
                GameAuthErrors.WorldUnavailable => StatusCode(503, receipt),
                _ => Unauthorized(receipt),
            };
        }
        catch (Exception error) when (error is RedisException or JsonException or CryptographicException)
        {
            logger.LogError(error, "An error occurred while redeeming a game join ticket");
            return StatusCode(503, JoinRedemptionReceipt.Failure(GameAuthErrors.ServiceUnavailable));
        }
    }
}
