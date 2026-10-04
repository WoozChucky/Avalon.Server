using Avalon.Common.GameAuth;
using System.Security.Cryptography;
using System.Text.Json;
using Avalon.Api.Contract;
using Avalon.Api.Middlewares;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StackExchange.Redis;

namespace Avalon.Api.Controllers;

[ApiController, AllowAnonymous, Route("game")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(GameAuthPolicy.MaximumControlBodyBytes), EnableRateLimiting(ApiRateLimiting.ClientAuthPolicy)]
public sealed class GameAdmissionController(GameAuthorizationService authorization, JoinTicketStore tickets,
    IGameServerAllocator allocator) : ControllerBase
{
    [HttpPost("worlds", Name = "GetGameWorlds")]
    [ProducesResponseType(typeof(IReadOnlyList<GameWorldDestination>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Worlds(GameContextCredentialRequest request, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest(new GameJoinReply(GameAuthErrors.HttpsRequired));
        try
        {
            var context = await authorization.GetContextAsync(request.GameContextCredential, false, cancellationToken);
            if (context?.AccountId is null) return Unauthorized(new GameJoinReply(GameAuthErrors.AccountRequired));
            return Ok(await allocator.ListAsync(context, cancellationToken));
        }
        catch (Exception error) when (error is RedisException or JsonException or CryptographicException)
        { return StatusCode(503, new GameJoinReply(GameAuthErrors.ServiceUnavailable)); }
    }

    [HttpPost("join-tickets", Name = "CreateGameJoinTicket")]
    [ProducesResponseType(typeof(GameJoinReply), StatusCodes.Status200OK)]
    public Task<IActionResult> Join(GameJoinRequest request, [FromHeader(Name = "Idempotency-Key")] Guid requestId,
        CancellationToken cancellationToken) => Issue(request, requestId, false, cancellationToken);

    [HttpPost("reconnect-tickets", Name = "CreateGameReconnectTicket")]
    [ProducesResponseType(typeof(GameJoinReply), StatusCodes.Status200OK)]
    public Task<IActionResult> Reconnect(GameJoinRequest request, [FromHeader(Name = "Idempotency-Key")] Guid requestId,
        CancellationToken cancellationToken) => Issue(request, requestId, true, cancellationToken);

    private async Task<IActionResult> Issue(GameJoinRequest request, Guid requestId, bool reconnect, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest(new GameJoinReply(GameAuthErrors.HttpsRequired));
        try
        {
            var reply = await tickets.IssueAsync(request.GameContextCredential, request.WorldId, request.CharacterId,
                requestId, request.ConfirmTakeover, reconnect, cancellationToken);
            return reply.Error switch
            {
                null => Ok(reply), GameAuthErrors.InvalidRequest => BadRequest(reply),
                GameAuthErrors.ActiveGameSession or GameAuthErrors.IdempotencyConflict or GameAuthErrors.ContextChanged => Conflict(reply),
                GameAuthErrors.WorldUnavailable => StatusCode(503, reply), _ => Unauthorized(reply),
            };
        }
        catch (Exception error) when (error is RedisException or JsonException or CryptographicException)
        { return StatusCode(503, new GameJoinReply(GameAuthErrors.ServiceUnavailable)); }
    }
}
