using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Avalon.Api.Contract;
using Avalon.Api.Identity.Authentication;
using Avalon.Common.GameAuth;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StackExchange.Redis;

namespace Avalon.Api.Identity.Controllers;

[ApiController, AllowAnonymous, Route("game")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(GameAuthPolicy.MaximumControlBodyBytes), EnableRateLimiting(ClientAuthRateLimiting.Policy)]
public sealed class GameAdmissionController(ILogger<GameAdmissionController> logger,
    GameAuthorizationService authorization, JoinTicketStore tickets, IGameServerAllocator allocator,
    GameApplicationAccessPolicy applications) : ControllerBase
{
    [HttpPost("worlds", Name = "GetGameWorlds")]
    [ProducesResponseType(typeof(IReadOnlyList<GameWorldDestination>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GameJoinReply), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Worlds(GameContextCredentialRequest request, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest(new GameJoinReply(GameAuthErrors.HttpsRequired));
        try
        {
            GameContextStanding standing =
                await authorization.GetContextStandingAsync(request.GameContextCredential, false, cancellationToken);
            // A current credential whose account may not play: 403 with its standing (#882).
            if (standing.AccountRefusal is { } refusal)
                return StatusCode(StatusCodes.Status403Forbidden, new GameJoinReply(refusal));
            GameContextRecord? context = standing.Context;
            if (context?.AccountId is null) return Unauthorized(new GameJoinReply(GameAuthErrors.AccountRequired));
            if (applications.RequiresLicenseForWorldListing(context.ApplicationKey))
            {
                context = await authorization.GetContextAsync(request.GameContextCredential, true, cancellationToken);
                if (context is null) return Unauthorized(new GameJoinReply(GameAuthErrors.AuthorizationRequired));
            }

            return Ok(await allocator.ListAsync(context, cancellationToken));
        }
        catch (Exception error) when (error is RedisException or RedisTimeoutException or JsonException or CryptographicException)
        {
            logger.LogError(error, "An error occurred while retrieving the game worlds");
            return StatusCode(503, new GameJoinReply(GameAuthErrors.ServiceUnavailable));
        }
    }

    [HttpPost("join-tickets", Name = "CreateGameJoinTicket")]
    [IdempotencyKeyRequired(typeof(GameJoinReply))]
    [ProducesResponseType(typeof(GameJoinReply), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GameJoinReply), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GameJoinReply), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(GameJoinReply), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GameJoinReply), StatusCodes.Status426UpgradeRequired)]
    [ProducesResponseType(typeof(GameJoinReply), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> Join(GameJoinRequest request, [FromHeader(Name = "Idempotency-Key")] Guid requestId,
        CancellationToken cancellationToken) => Issue(request, requestId, false, cancellationToken);

    [HttpPost("reconnect-tickets", Name = "CreateGameReconnectTicket")]
    [IdempotencyKeyRequired(typeof(GameJoinReply))]
    [ProducesResponseType(typeof(GameJoinReply), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GameJoinReply), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GameJoinReply), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(GameJoinReply), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GameJoinReply), StatusCodes.Status426UpgradeRequired)]
    [ProducesResponseType(typeof(GameJoinReply), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> Reconnect(GameJoinRequest request, [FromHeader(Name = "Idempotency-Key")] Guid requestId,
        CancellationToken cancellationToken) => Issue(request, requestId, true, cancellationToken);

    private async Task<IActionResult> Issue(GameJoinRequest request, Guid requestId, bool reconnect, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest(new GameJoinReply(GameAuthErrors.HttpsRequired));
        try
        {
            GameJoinReply reply = await tickets.IssueAsync(request.GameContextCredential, request.WorldId,
                request.CharacterId,
                requestId, request.ConfirmTakeover, reconnect, cancellationToken);
            // A retryable refusal (503) apart from a permanent one (#1053): a client too old is told to update (426,
            // with the version required), one the world will never take is refused (403, 404, 400). A world at its
            // player limit is a retryable one too, with when to ask again (#967).
            return reply.Error switch
            {
                null => Ok(reply),
                GameAuthErrors.InvalidRequest or GameAuthErrors.UnsupportedProtocol => BadRequest(reply),
                GameAuthErrors.ActiveGameSession or GameAuthErrors.IdempotencyConflict or GameAuthErrors.ContextChanged
                    => Conflict(reply),
                GameAuthErrors.WorldUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, reply),
                GameAuthErrors.WorldFull => WorldFull(reply),
                GameAuthErrors.ClientOutdated => UpgradeRequired(reply),
                GameAuthErrors.WorldNotAllowed => StatusCode(StatusCodes.Status403Forbidden, reply),
                GameAuthErrors.CharacterNotFound => NotFound(reply),
                _ when GameAuthErrors.IsAccountStanding(reply.Error) => StatusCode(StatusCodes.Status403Forbidden,
                    reply),
                _ => Unauthorized(reply),
            };
        }
        catch (Exception error) when (error is RedisException or RedisTimeoutException or JsonException or CryptographicException)
        {
            logger.LogError(error, "An error occurred while issuing a game join ticket");
            return StatusCode(503, new GameJoinReply(GameAuthErrors.ServiceUnavailable));
        }
    }

    /// <summary>
    /// 503 <c>WORLD_FULL</c> with when to ask again (#967), jittered between 45 and 75 s so the players refused together
    /// spread their retries. Not a secret: the shared pseudo-random source is enough.
    /// </summary>
    private ObjectResult WorldFull(GameJoinReply reply)
    {
        int seconds = Random.Shared.Next((int)GameAuthPolicy.WorldFullRetryAfterMin.TotalSeconds,
            (int)GameAuthPolicy.WorldFullRetryAfterMax.TotalSeconds + 1);
        Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        return StatusCode(StatusCodes.Status503ServiceUnavailable, reply);
    }

    /// <summary>
    /// 426 with the advisory <c>Upgrade</c> header the status calls for, naming the client version required as a
    /// product token (<c>avalon-client/1.0.0</c>); the body's <c>requiredVersion</c> says the same.
    /// </summary>
    private ObjectResult UpgradeRequired(GameJoinReply reply)
    {
        if (reply.RequiredVersion is { } required) Response.Headers.Upgrade = "avalon-client/" + required;
        return StatusCode(StatusCodes.Status426UpgradeRequired, reply);
    }
}
