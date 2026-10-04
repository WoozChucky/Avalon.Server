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
[RequestSizeLimit(4096), EnableRateLimiting(ApiRateLimiting.ClientAuthPolicy)]
public sealed class GameAdmissionController(GameAuthorizationService authorization, JoinTicketStore tickets,
    IGameServerAllocator allocator) : ControllerBase
{
    [HttpPost("worlds", Name = "GetGameWorlds")]
    [ProducesResponseType(typeof(IReadOnlyList<GameWorldDestination>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Worlds(GameContextCredentialRequest request, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest(new GameJoinReply("HTTPS_REQUIRED"));
        try
        {
            var context = await authorization.GetContextAsync(request.GameContextCredential, false, cancellationToken);
            if (context?.AccountId is null) return Unauthorized(new GameJoinReply("ACCOUNT_REQUIRED"));
            return Ok(await allocator.ListAsync(context, cancellationToken));
        }
        catch (Exception error) when (error is RedisException or JsonException or CryptographicException)
        { return StatusCode(503, new GameJoinReply("SERVICE_UNAVAILABLE")); }
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
        if (!Request.IsHttps) return BadRequest(new GameJoinReply("HTTPS_REQUIRED"));
        try
        {
            var reply = await tickets.IssueAsync(request.GameContextCredential, request.WorldId, request.CharacterId,
                requestId, request.ConfirmTakeover, reconnect, cancellationToken);
            return reply.Error switch
            {
                null => Ok(reply), "INVALID_REQUEST" => BadRequest(reply),
                "ACTIVE_GAME_SESSION" or "IDEMPOTENCY_CONFLICT" or "CONTEXT_CHANGED" => Conflict(reply),
                "WORLD_UNAVAILABLE" => StatusCode(503, reply), _ => Unauthorized(reply),
            };
        }
        catch (Exception error) when (error is RedisException or JsonException or CryptographicException)
        { return StatusCode(503, new GameJoinReply("SERVICE_UNAVAILABLE")); }
    }
}
