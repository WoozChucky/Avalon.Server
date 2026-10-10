using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalon.Api.Contract;
using Avalon.Api.Identity.Authentication;
using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StackExchange.Redis;

namespace Avalon.Api.Identity.Controllers;

[ApiController, AllowAnonymous, Route("client/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(GameAuthPolicy.MaximumBodyBytes), EnableRateLimiting(ClientAuthRateLimiting.Policy)]
public sealed class GameAuthController(GameAuthorizationService authorization, ILogger<GameAuthController>? authLogger = null) : ControllerBase
{
    [HttpPost("provider-attempts", Name = "CreateProviderGameAuthAttempt")]
    [ProducesResponseType(typeof(ProviderAuthAttemptReply), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GameAuthReply), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ProviderAttempt(GameProviderAttemptRequest request, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return LogResult(BadRequest(GameAuthReply.Failure(GameAuthErrors.HttpsRequired)), nameof(ProviderAttempt));
        if (request.ProtocolVersion != GameWorkloadConfiguration.ClientProtocolVersion)
            return LogResult(BadRequest(GameAuthReply.Failure(GameAuthErrors.UnsupportedProtocol)), nameof(ProviderAttempt));
        try
        {
            (AuthAttemptReply? reply, string? refusal) = await authorization.CreateProviderAttemptAsync(request.ApplicationKey,
                request.ProtocolVersion, request.ClientRunId, request.LinkChallenge, request.GameContextCredential, 0, cancellationToken);
            // A context credential naming an account that may not play: 403 with its standing (#882).
            if (refusal is not null) return LogResult(StatusCode(StatusCodes.Status403Forbidden, GameAuthReply.Failure(refusal)), nameof(ProviderAttempt));
            return LogResult(reply is null ? BadRequest(GameAuthReply.Failure(GameAuthErrors.InvalidAttempt)) :
                Ok(new ProviderAuthAttemptReply(reply.AttemptCredential, reply.ExpectedSteamIdentity, reply.ExpiresAt)), nameof(ProviderAttempt));
        }
        catch (RedisException) { return LogResult(StatusCode(503, GameAuthReply.Failure(GameAuthErrors.ServiceUnavailable)), nameof(ProviderAttempt)); }
    }

    [HttpPost("store/proof", Name = "AuthenticateProviderGame")]
    [ProducesResponseType(typeof(GameAuthReply), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GameAuthReply), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ProviderProof(GameProviderProofRequest request, [FromHeader(Name = "Idempotency-Key")] Guid requestId,
        CancellationToken cancellationToken) => Execute(() => authorization.AuthenticateProviderAsync(request.Provider,
            request.AttemptCredential, request.Proof, requestId, cancellationToken, HttpContext.Connection.RemoteIpAddress?.ToString()));

    [HttpPost("handoffs/redeem", Name = "RedeemGameAuthHandoff")]
    [ProducesResponseType(typeof(GameAuthReply), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GameAuthReply), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Handoff(GameHandoffRequest request, [FromHeader(Name = "Idempotency-Key")] Guid requestId,
        CancellationToken cancellationToken) => Execute(() => authorization.RedeemHandoffAsync(
            request.AttemptCredential, request.HandoffTicket, requestId, cancellationToken));

    [HttpPost("game-context/refresh", Name = "RefreshGameAuthContext")]
    [ProducesResponseType(typeof(GameAuthReply), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GameAuthReply), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Refresh(GameContextRefreshRequest request, [FromHeader(Name = "Idempotency-Key")] Guid requestId,
        CancellationToken cancellationToken) => Execute(() => authorization.RefreshAsync(request.GameContextRefreshToken, requestId, cancellationToken));

    [HttpPost("game-context/logout", Name = "LogoutGameAuthContext")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(GameAuthReply), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Logout(GameContextCredentialRequest request, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest(GameAuthReply.Failure(GameAuthErrors.HttpsRequired));
        try
        {
            // A context still live after every retry is answered 409 IN_PROGRESS, never 204: the caller logs out again.
            return await authorization.LogoutAsync(request.GameContextCredential, cancellationToken) == GameContextLogout.Contended
                ? StatusCode(409, GameAuthReply.Failure(GameAuthErrors.InProgress))
                : NoContent();
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
            LinkProposalReply reply = await links.ProposalAsync(request.GameContextCredential, request.PkceVerifier, cancellationToken);
            return reply.Error is null ? Ok(reply) : Unauthorized(reply);
        }
        catch (Exception error) when (error is RedisException or JsonException or CryptographicException)
        { return StatusCode(503, GameAuthReply.Failure(GameAuthErrors.ServiceUnavailable)); }
    }

    [HttpPost("links/complete", Name = "CompleteGameAccountLink")]
    [ProducesResponseType(typeof(GameAuthReply), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GameAuthReply), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> CompleteLink(GameLinkCompleteRequest request, [FromServices] PendingLinkStore links,
        [FromServices] ILogger<GameAuthController> logger, [FromHeader(Name = "Idempotency-Key")] Guid requestId,
        CancellationToken cancellationToken) => Execute(async () =>
    {
        GameAuthReply reply = await authorization.CompleteAccountLinkAsync(links, request.GameContextCredential,
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
            GameAuthReply reply = await action();
            if (reply.GameContextCredential is not null || reply.Error is null) return LogResult(Ok(reply), operation);
            return LogResult(reply.Error switch
            {
                GameAuthErrors.InProgress => StatusCode(409, reply),
                GameAuthErrors.ProviderUnavailable => StatusCode(503, reply),
                GameAuthErrors.AccountMismatch => Conflict(reply),
                // The caller proved who they are; the account may not play (#882).
                _ when GameAuthErrors.IsAccountStanding(reply.Error) => StatusCode(StatusCodes.Status403Forbidden, reply),
                _ => Unauthorized(reply),
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
