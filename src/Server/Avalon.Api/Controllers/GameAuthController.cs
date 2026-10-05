using Avalon.Common.GameAuth;
using System.Runtime.CompilerServices;
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
public sealed class GameAuthController(GameAuthorizationService authorization, ILogger<GameAuthController>? authLogger = null) : ControllerBase
{
    [HttpPost("provider-attempts", Name = "CreateProviderGameAuthAttempt")]
    [ProducesResponseType(typeof(ProviderAuthAttemptReply), StatusCodes.Status200OK)]
    public async Task<IActionResult> ProviderAttempt(GameProviderAttemptRequest request, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return LogResult(BadRequest(GameAuthReply.Failure(GameAuthErrors.HttpsRequired)), nameof(ProviderAttempt));
        if (request.ProtocolVersion != GameWorkloadConfiguration.ClientProtocolVersion)
            return LogResult(BadRequest(GameAuthReply.Failure(GameAuthErrors.UnsupportedProtocol)), nameof(ProviderAttempt));
        try
        {
            var reply = await authorization.CreateProviderAttemptAsync(request.ApplicationKey, request.ProtocolVersion,
                request.ClientRunId, request.LinkChallenge, request.GameContextCredential, 0, cancellationToken);
            return LogResult(reply is null ? BadRequest(GameAuthReply.Failure(GameAuthErrors.InvalidAttempt)) :
                Ok(new ProviderAuthAttemptReply(reply.AttemptCredential, reply.ExpectedSteamIdentity, reply.ExpiresAt)), nameof(ProviderAttempt));
        }
        catch (RedisException) { return LogResult(StatusCode(503, GameAuthReply.Failure(GameAuthErrors.ServiceUnavailable)), nameof(ProviderAttempt)); }
    }

    [HttpPost("store/proof", Name = "AuthenticateProviderGame")]
    [ProducesResponseType(typeof(GameAuthReply), StatusCodes.Status200OK)]
    public Task<IActionResult> ProviderProof(GameProviderProofRequest request, [FromHeader(Name = "Idempotency-Key")] Guid requestId,
        CancellationToken cancellationToken) => Execute(() => authorization.AuthenticateProviderAsync(request.Provider,
            request.AttemptCredential, request.Proof, requestId, cancellationToken, HttpContext.Connection.RemoteIpAddress?.ToString()));

    [HttpPost("handoffs/redeem", Name = "RedeemGameAuthHandoff")]
    [ProducesResponseType(typeof(GameAuthReply), StatusCodes.Status200OK)]
    public Task<IActionResult> Handoff(GameHandoffRequest request, [FromHeader(Name = "Idempotency-Key")] Guid requestId,
        CancellationToken cancellationToken) => Execute(() => authorization.RedeemHandoffAsync(
            request.AttemptCredential, request.HandoffTicket, requestId, cancellationToken));

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

    private async Task<IActionResult> Execute(Func<Task<GameAuthReply>> action, [CallerMemberName] string operation = "")
    {
        if (!Request.IsHttps) return LogResult(BadRequest(GameAuthReply.Failure(GameAuthErrors.HttpsRequired)), operation);
        try
        {
            var reply = await action();
            if (reply.GameContextCredential is not null || reply.Error is null) return LogResult(Ok(reply), operation);
            return LogResult(reply.Error switch
            {
                GameAuthErrors.InProgress => StatusCode(409, reply), GameAuthErrors.ProviderUnavailable => StatusCode(503, reply),
                GameAuthErrors.AccountMismatch => Conflict(reply), _ => Unauthorized(reply),
            }, operation);
        }
        catch (Exception error) when (error is RedisException or JsonException or CryptographicException)
        { return LogResult(StatusCode(503, GameAuthReply.Failure(GameAuthErrors.ServiceUnavailable)), operation); }
    }

    private ObjectResult LogResult(ObjectResult result, string operation)
    {
        authLogger?.LogInformation("Game authentication {Operation} responded {StatusCode} with {ErrorCode}",
            operation, result.StatusCode, (result.Value as GameAuthReply)?.Error ?? "none");
        return result;
    }
}
