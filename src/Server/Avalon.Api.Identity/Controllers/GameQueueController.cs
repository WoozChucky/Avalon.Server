using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;
using Avalon.Api.Contract;
using Avalon.Api.Identity.Authentication;
using Avalon.Api.Identity.Services;
using Avalon.Common.GameAuth;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StackExchange.Redis;

namespace Avalon.Api.Identity.Controllers;

/// <summary>
/// A full world's line (#967): a waiter checks in and leaves with its game context credential, checked on Redis alone.
/// Out of the shared limiter (<see cref="DisableRateLimitingAttribute"/>), whose per-source partition would throttle the
/// waiters behind one address, and limited by its own per-source ceiling, a bound on the credential reads of a source that
/// keeps failing, and a fair share per account instead (<see cref="QueueRateLimiter"/>). No
/// <c>Idempotency-Key</c>: both calls are idempotent by nature.
/// </summary>
[ApiController, AllowAnonymous, DisableRateLimiting, Route("game/queue")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(GameAuthPolicy.MaximumControlBodyBytes)]
public sealed class GameQueueController(ILogger<GameQueueController> logger, GameAuthorizationService authorization,
    GameQueueService queue, QueueRateLimiter limiter) : ControllerBase
{
    /// <summary>Where the account stands in world <c>worldId</c>'s line, or its turn; when to check in again.</summary>
    [HttpPost("status", Name = "GetGameQueueStatus")]
    [ProducesResponseType(typeof(GameQueueStatusReply), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GameQueueStatusReply), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GameQueueStatusReply), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(GameQueueStatusReply), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(GameQueueStatusReply), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> Status(GameQueueRequest request, CancellationToken cancellationToken) =>
        RunAsync(request, account => queue.StatusAsync(account, request.WorldId, cancellationToken), cancellationToken);

    /// <summary>Leaves world <c>worldId</c>'s line, giving back a turn not yet used. Safe to repeat.</summary>
    [HttpPost("leave", Name = "LeaveGameQueue")]
    [ProducesResponseType(typeof(GameQueueStatusReply), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GameQueueStatusReply), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GameQueueStatusReply), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(GameQueueStatusReply), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(GameQueueStatusReply), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> Leave(GameQueueRequest request, CancellationToken cancellationToken) =>
        RunAsync(request, account => queue.LeaveAsync(account, request.WorldId, cancellationToken), cancellationToken);

    private async Task<IActionResult> RunAsync(GameQueueRequest request, Func<long, Task<GameQueueStatusReply>> action,
        CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest(new GameQueueStatusReply { Error = GameAuthErrors.HttpsRequired });
        // The source's ceiling, before anything is read.
        if (limiter.TryAdmitSource(HttpContext) is { } sourceRetryAfter) return TooMany(sourceRetryAfter);
        try
        {
            GameContextRecord? context;
            // A source past its failures reads a few credentials at a time, never none, so a neighbour behind the same
            // address cannot shut its honest waiters out.
            using (RateLimitLease? read = await limiter.StartCredentialReadAsync(HttpContext, cancellationToken))
            {
                if (read is null) return TooMany(QueueRateLimiter.Segment);
                context = await authorization.GetCurrentContextAsync(request.GameContextCredential, cancellationToken);
            }

            if (context?.AccountId is not { } accountId)
            {
                limiter.CredentialFailed(HttpContext);
                return Unauthorized(new GameQueueStatusReply { Error = GameAuthErrors.AuthorizationRequired });
            }

            if (limiter.TryAcquire(accountId) is { } retryAfter) return TooMany(retryAfter);
            return Ok(await action(accountId));
        }
        catch (Exception error) when (error is RedisException or RedisTimeoutException or DbException or JsonException or CryptographicException)
        {
            logger.LogError(error, "An error occurred while answering a game queue call");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new GameQueueStatusReply { Error = GameAuthErrors.ServiceUnavailable });
        }
    }

    /// <summary>429 with when to ask again, and the login lockout's word, as the shared limiter answers.</summary>
    private ObjectResult TooMany(TimeSpan retryAfter)
    {
        Response.Headers.RetryAfter = Math.Max(1, (long)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return StatusCode(StatusCodes.Status429TooManyRequests, new GameQueueStatusReply { Error = "LOCKED" });
    }
}
