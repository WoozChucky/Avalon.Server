using Avalon.Common.GameAuth;
using System.Security.Cryptography;
using System.Text.Json;
using Avalon.Api.Contract;
using Avalon.Configuration;
using Avalon.Api.Middlewares;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StackExchange.Redis;

namespace Avalon.Api.Controllers;

[ApiController, AllowAnonymous, Route("client/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(GameAuthPolicy.MaximumBodyBytes), EnableRateLimiting(ApiRateLimiting.ClientAuthPolicy)]
public sealed class GameAuthController(GameAuthorizationService authorization) : ControllerBase
{
    [HttpPost("attempts", Name = "CreateGameAuthAttempt")]
    [ProducesResponseType(typeof(AuthAttemptReply), StatusCodes.Status200OK)]
    public async Task<IActionResult> Attempt(GameAttemptRequest request, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest(GameAuthReply.Failure(GameAuthErrors.HttpsRequired));
        if (request.ProtocolVersion != GameWorkloadConfiguration.ClientProtocolVersion)
            return BadRequest(GameAuthReply.Failure(GameAuthErrors.UnsupportedProtocol));
        try
        {
            var reply = await authorization.CreateAttemptAsync(request.ChannelHint, request.ProtocolVersion,
                request.ClientRunId, request.LinkChallenge, request.GameContextCredential, request.SteamAppId, cancellationToken);
            return reply is null ? BadRequest(GameAuthReply.Failure(GameAuthErrors.InvalidAttempt)) : Ok(reply);
        }
        catch (RedisException) { return StatusCode(503, GameAuthReply.Failure(GameAuthErrors.ServiceUnavailable)); }
    }

    [HttpPost("handoffs/redeem", Name = "RedeemGameAuthHandoff")]
    [ProducesResponseType(typeof(GameAuthReply), StatusCodes.Status200OK)]
    public Task<IActionResult> Handoff(GameHandoffRequest request, [FromHeader(Name = "Idempotency-Key")] Guid requestId,
        CancellationToken cancellationToken) => Execute(() => authorization.RedeemHandoffAsync(
            request.AttemptCredential, request.HandoffTicket, requestId, cancellationToken));

    [HttpPost("store/steam", Name = "AuthenticateSteamGame")]
    [ProducesResponseType(typeof(GameAuthReply), StatusCodes.Status200OK)]
    public Task<IActionResult> Steam(SteamGameProofRequest request, [FromHeader(Name = "Idempotency-Key")] Guid requestId,
        CancellationToken cancellationToken) => Execute(() => authorization.AuthenticateSteamAsync(
            request.AttemptCredential, request.TicketHex, requestId, cancellationToken, HttpContext.Connection.RemoteIpAddress?.ToString()));

    [HttpPost("game-context/refresh", Name = "RefreshGameAuthContext")]
    [ProducesResponseType(typeof(GameAuthReply), StatusCodes.Status200OK)]
    public Task<IActionResult> Refresh(GameContextRefreshRequest request, [FromHeader(Name = "Idempotency-Key")] Guid requestId,
        CancellationToken cancellationToken) => Execute(() => authorization.RefreshAsync(request.GameContextRefreshToken, requestId, cancellationToken));

    [HttpPost("game-context/logout", Name = "LogoutGameAuthContext")]
    public async Task<IActionResult> Logout(GameContextCredentialRequest request, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest(GameAuthReply.Failure(GameAuthErrors.HttpsRequired));
        try
        {
            await authorization.LogoutAsync(request.GameContextCredential, cancellationToken);
            return NoContent();
        }
        catch (RedisException) { return StatusCode(503, GameAuthReply.Failure(GameAuthErrors.ServiceUnavailable)); }
    }

    [HttpPost("links/proposal", Name = "GetGameAccountLinkProposal")]
    [ProducesResponseType(typeof(LinkProposalReply), StatusCodes.Status200OK)]
    public async Task<IActionResult> LinkProposal(GameLinkProposalRequest request, [FromServices] PendingLinkStore links,
        CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest(GameAuthReply.Failure(GameAuthErrors.HttpsRequired));
        try
        {
            var reply = await links.ProposalAsync(request.GameContextCredential, request.PkceVerifier, cancellationToken);
            return reply.Error is null ? Ok(reply) : Unauthorized(reply);
        }
        catch (Exception error) when (error is RedisException or JsonException or CryptographicException)
        { return StatusCode(503, GameAuthReply.Failure(GameAuthErrors.ServiceUnavailable)); }
    }

    [HttpPost("links/complete", Name = "CompleteGameAccountLink")]
    [ProducesResponseType(typeof(GameAuthReply), StatusCodes.Status200OK)]
    public Task<IActionResult> CompleteLink(GameLinkCompleteRequest request, [FromServices] PendingLinkStore links,
        [FromServices] ILogger<GameAuthController> logger, [FromHeader(Name = "Idempotency-Key")] Guid requestId,
        CancellationToken cancellationToken) => Execute(async () =>
    {
        var reply = await authorization.CompleteAccountLinkAsync(links, request.GameContextCredential,
            request.ConsentCode, request.PkceVerifier, requestId, request.Accepted, cancellationToken);
        if (reply.AccountId is not null && reply.GameContextCredential is not null)
            logger.LogInformation("Store identity linked for account {AccountId}, provider {Provider}", reply.AccountId, StoreProviders.Steam);
        return reply;
    });

    private async Task<IActionResult> Execute(Func<Task<GameAuthReply>> action)
    {
        if (!Request.IsHttps) return BadRequest(GameAuthReply.Failure(GameAuthErrors.HttpsRequired));
        try
        {
            var reply = await action();
            if (reply.GameContextCredential is not null || reply.Error is null) return Ok(reply);
            return reply.Error switch
            {
                GameAuthErrors.InProgress => StatusCode(409, reply), GameAuthErrors.ProviderUnavailable => StatusCode(503, reply),
                GameAuthErrors.AccountMismatch => Conflict(reply), _ => Unauthorized(reply),
            };
        }
        catch (Exception error) when (error is RedisException or JsonException or CryptographicException)
        { return StatusCode(503, GameAuthReply.Failure(GameAuthErrors.ServiceUnavailable)); }
    }
}
