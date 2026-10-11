using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Avalon.Api.Contract;
using Avalon.Api.Identity.Authentication;
using Avalon.Api.Identity.Services;
using Avalon.Api.Services;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace Avalon.Api.Identity.Controllers;

public sealed partial class InternalGameAdmissionController
{
    [HttpPost("sessions/activate", Name = "ActivateGameSession")]
    [ProducesResponseType(typeof(GameSessionLeaseReply), StatusCodes.Status200OK)]
    public Task<IActionResult> Activate(GameSessionControlRequest request, [FromServices] GameSessionFenceService sessions,
        CancellationToken cancellationToken) => SessionAction(request, sessions.ActivateAsync, cancellationToken);

    [HttpPost("sessions/heartbeat", Name = "RenewGameSession")]
    [ProducesResponseType(typeof(GameSessionLeaseReply), StatusCodes.Status200OK)]
    public Task<IActionResult> Heartbeat(GameSessionControlRequest request, [FromServices] GameSessionFenceService sessions,
        CancellationToken cancellationToken) => SessionAction(request, sessions.HeartbeatAsync, cancellationToken);

    [HttpPost("sessions/end", Name = "EndGameSession")]
    [ProducesResponseType(typeof(GameSessionLeaseReply), StatusCodes.Status200OK)]
    public Task<IActionResult> End(GameSessionControlRequest request, [FromServices] GameSessionFenceService sessions,
        CancellationToken cancellationToken) => SessionAction(request, sessions.EndAsync, cancellationToken);

    private async Task<IActionResult> SessionAction(GameSessionControlRequest request,
        Func<string, AccountId, Guid, long, CancellationToken, Task<GameSessionLeaseReply>> action, CancellationToken cancellationToken)
    {
        string? serverId = User.FindFirst(GameServerAuthHandler.ServerIdClaim)?.Value;
        if (!Request.IsHttps || serverId is null) return Unauthorized(GameSessionLeaseReply.Failure(GameAuthErrors.WorkloadAuthenticationRequired));
        if (!long.TryParse(request.AccountId, NumberStyles.None, CultureInfo.InvariantCulture, out long accountId) || accountId <= 0 ||
            accountId.ToString(CultureInfo.InvariantCulture) != request.AccountId ||
            !long.TryParse(request.FencingToken, NumberStyles.None, CultureInfo.InvariantCulture, out long fence) || fence <= 0 ||
            fence.ToString(CultureInfo.InvariantCulture) != request.FencingToken || request.GameSessionId == Guid.Empty)
        {
            return BadRequest(GameSessionLeaseReply.Failure(GameAuthErrors.InvalidRequest));
        }

        try
        {
            GameSessionLeaseReply reply = await action(serverId, new AccountId(accountId), request.GameSessionId, fence,
                cancellationToken);
            return reply.Error switch
            {
                null => Ok(reply),
                GameAuthErrors.BarrierPending or GameAuthErrors.ServiceUnavailable => StatusCode(503, reply),
                _ => Unauthorized(reply),
            };
        }
        catch (Exception error) when (error is DbException or RedisException or JsonException or CryptographicException)
        {
            logger.LogError(error, "An error occurred while performing a game session action '{Action}'", action.Method.Name);
            return StatusCode(503, GameSessionLeaseReply.Failure(GameAuthErrors.ServiceUnavailable));
        }
    }
}
