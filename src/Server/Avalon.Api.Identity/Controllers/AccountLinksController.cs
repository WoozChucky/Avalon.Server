using System.Security.Cryptography;
using System.Text.Json;
using Avalon.Api.Contract;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Controllers;
using Avalon.Api.Identity.Authentication;
using Avalon.Api.Identity.Authentication.Jwt;
using Avalon.Api.Identity.Services;
using Avalon.Common.GameAuth;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StackExchange.Redis;

namespace Avalon.Api.Identity.Controllers;

[ApiController, Authorize(Policy = AvalonRoles.Player), Route("account/links")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(GameAuthPolicy.MaximumBodyBytes), EnableRateLimiting(ClientAuthRateLimiting.Policy)]
public sealed class AccountLinksController(PendingLinkStore links, AccountLinkReauthentication recent,
    IAuthContext auth) : BaseController
{
    // Linking is an interactive browser action. Script tokens and launcher sessions cannot consent.
    private bool Browser => Request.IsHttps && auth.Account is not null &&
        !User.HasClaim(c => c.Type is "pat_id" or JwtUtils.LauncherFamilyClaim);

    [HttpGet("{pendingLinkId:guid}", Name = "GetPendingAccountLink")]
    [ProducesResponseType(typeof(LinkBrowserReply), StatusCodes.Status200OK)]
    public Task<IActionResult> Info(Guid pendingLinkId, CancellationToken cancellationToken) => Execute(async () =>
        await links.InfoAsync(pendingLinkId, auth.Account!.Id, cancellationToken));

    [HttpPost("confirm", Name = "ConfirmAccountStoreLink")]
    [ProducesResponseType(typeof(LinkBrowserReply), StatusCodes.Status200OK)]
    public Task<IActionResult> Confirm(AccountLinkConfirmRequest request,
        [FromHeader(Name = "Idempotency-Key")] Guid requestId, CancellationToken cancellationToken) => Execute(async () =>
    {
        if (!request.Confirmed || requestId == Guid.Empty || request.PendingLinkId == Guid.Empty)
            return new(GameAuthStates.Pending, GameAuthErrors.ConfirmationRequired);
        LinkReauthenticated proof = await recent.RequireAsync(auth.Account!, request.CurrentPassword, request.MfaCode,
            SourceAddress, cancellationToken);
        if (proof.Error is not null) return new(GameAuthStates.Pending, proof.Error);
        return await links.ConfirmAsync(request.PendingLinkId, auth.Account!.Id, proof.CredentialsVersion,
            proof.SessionEpoch, proof.ConfirmedMfaId, requestId, cancellationToken);
    });

    [HttpPost("{pendingLinkId:guid}/cancel", Name = "CancelAccountStoreLink")]
    [ProducesResponseType(typeof(LinkBrowserReply), StatusCodes.Status200OK)]
    public Task<IActionResult> Cancel(Guid pendingLinkId, CancellationToken cancellationToken) => Execute(async () =>
        await links.CancelAsync(pendingLinkId, auth.Account!.Id, cancellationToken)
            ? new("canceled") : new(GameAuthStates.Pending, GameAuthErrors.InvalidLink));

    private async Task<IActionResult> Execute(Func<Task<LinkBrowserReply>> action)
    {
        if (!Browser) return Unauthorized();
        try
        {
            LinkBrowserReply reply = await action();
            return reply.Error is null ? Ok(reply) : BadRequest(reply);
        }
        catch (Exception error) when (error is RedisException or JsonException or CryptographicException)
        { return StatusCode(503, new LinkBrowserReply(GameAuthStates.Pending, GameAuthErrors.ServiceUnavailable)); }
    }
}
