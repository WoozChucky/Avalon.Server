using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Avalon.Api.Controllers;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Controllers;
using Avalon.Api.Identity.Authentication;
using Avalon.Api.Identity.Authentication.Jwt;
using Avalon.Api.Identity.Services;
using Avalon.Api.Services;
using Avalon.Common.GameAuth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Avalon.Api.Identity.Controllers;

[ApiController, Authorize(Policy = AvalonRoles.Player), Route("account/links/steam")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(GameAuthPolicy.MaximumBodyBytes), EnableRateLimiting(ClientAuthRateLimiting.Policy)]
public sealed class SteamWebLinksController(SteamWebLinkStore links, AccountLinkReauthentication recent,
    AccountConsolidationService consolidation, IAccountConsolidationRepository operations,
    IExternalIdentityRepository identities, IAccountRepository accounts, IMfaSetupRepository mfaSetups, IAuthContext auth,
    IOptions<SteamWebLinkOptions> trusted, TimeProvider clock) : BaseController
{
    private string BrowserSession => User.FindFirstValue(JwtRegisteredClaimNames.Jti) ?? string.Empty;
    private bool Browser => Request.IsHttps && auth.Account is not null && BrowserSession.Length != 0 &&
        !User.HasClaim(c => c.Type is "pat_id" or JwtUtils.LauncherFamilyClaim);
    private string Cookie(Guid id) => Request.Cookies[SteamWebLinkRegistration.CookieName(id)] ?? string.Empty;

    [HttpPost("start", Name = "StartWebsiteSteamLink")]
    [ProducesResponseType(typeof(SteamWebLinkReply), 200)]
    public Task<IActionResult> Start([FromHeader(Name = "Idempotency-Key")] Guid requestId, CancellationToken ct) => Execute(async () =>
    {
        SteamWebLinkStart? start = await links.StartAsync(requestId, auth.Account!, BrowserSession, ct);
        if (start is null) return new(GameAuthStates.Pending, GameAuthErrors.InvalidLink);
        Response.Cookies.Append(SteamWebLinkRegistration.CookieName(start.Id), start.Cookie, new()
        { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", MaxAge = GameAuthPolicy.WebConfirmationLifetime, IsEssential = true });
        return new("created") { TransactionId = start.Id.ToString("N"), ChallengeUrl = trusted.Value.ChallengeUrl(start.Id) };
    });

    [AllowAnonymous, HttpGet("challenge/{id:guid}")]
    public async Task<IActionResult> Challenge(Guid id, CancellationToken ct)
    {
        if (!Request.IsHttps) return Unauthorized();
        SteamWebLinkRecord? record = await links.ReadCallbackAsync(id, Cookie(id), ct);
        Account? root = record is null ? null : await accounts.FindByIdAsync(record.AccountId, false, ct);
        if (root is null || root.GameplayConsolidationId is not null || root.IsLockedAt(clock.GetUtcNow().UtcDateTime) || root.Status != Avalon.Domain.Auth.AccountStatus.Active ||
            root.CredentialsVersion != record!.CredentialsVersion || root.SessionEpoch != record.SessionEpoch ||
            !await links.ChallengeAsync(id, Cookie(id), ct))
        {
            return BadRequest(new SteamWebLinkReply(GameAuthStates.Pending, GameAuthErrors.InvalidLink));
        }

        var properties = new AuthenticationProperties { RedirectUri = trusted.Value.ResultUrl(id) };
        properties.Items[SteamWebLinkRegistration.TransactionProperty] = id.ToString("N");
        return Challenge(properties, SteamWebLinkOptions.Scheme);
    }

    [HttpGet("{id:guid}", Name = "GetWebsiteSteamLink")]
    [ProducesResponseType(typeof(SteamWebLinkReply), 200)]
    public Task<IActionResult> Info(Guid id, CancellationToken ct) => Execute(async () =>
    {
        SteamWebLinkRecord? record = await links.ReadBoundAsync(id, auth.Account!.Id, BrowserSession, Cookie(id), ct);
        if (record is null) return new(GameAuthStates.Pending, GameAuthErrors.InvalidLink);
        AccountConsolidation? operation = await operations.FindAsync(id, ct);
        if (operation is not null) return new("consolidating") { Consolidation = await consolidation.StatusAsync(id, auth.Account.Id, ct) };
        ExternalIdentity? identity = record.SteamSubject is null ? null : await identities.FindAsync(StoreProviders.Steam, record.SteamSubject, ct);
        if (record.State == "committing" && identity is not null && identity.AccountId != auth.Account.Id && !record.ConsolidationConsent)
            return new(GameAuthStates.Pending, GameAuthErrors.SteamLinkChangedStartAgain);
        if (record.State == "committing" && identity?.AccountId == auth.Account.Id) return new("linked");
        MFASetup? mfa = await mfaSetups.FindByAccountIdAsync(auth.Account.Id, ct);
        return new(record.State) { ConfirmationId = record.ConfirmationId?.ToString("N"), Username = auth.Account.Username, SteamId = record.SteamSubject, RequiresMfa = mfa?.Status == Avalon.Domain.Auth.MfaSetupStatus.Confirmed, TransactionId = id.ToString("N"), RequiresConsolidation = identity is not null && identity.AccountId != auth.Account.Id, ProofExpiresAt = record.ProofExpiresAt };
    });

    [HttpPost("confirm", Name = "ConfirmWebsiteSteamLink")]
    [ProducesResponseType(typeof(SteamWebLinkReply), 200)]
    public Task<IActionResult> Confirm(SteamWebLinkConfirmation request,
        [FromHeader(Name = "Idempotency-Key")] Guid requestId, CancellationToken ct) => Execute(async () =>
    {
        if (!request.Confirmed || requestId == Guid.Empty || request.TransactionId == Guid.Empty) return new(GameAuthStates.Pending, GameAuthErrors.ConfirmationRequired);
        SteamWebLinkRecord? bound = await links.ReadBoundAsync(request.TransactionId, auth.Account!.Id, BrowserSession, Cookie(request.TransactionId), ct);
        if (bound is null) return new(GameAuthStates.Pending, GameAuthErrors.InvalidLink);
        ExternalIdentity? currentIdentity = bound.SteamSubject is null ? null : await identities.FindAsync(StoreProviders.Steam, bound.SteamSubject, ct);
        if (currentIdentity is not null && currentIdentity.AccountId != auth.Account.Id && !request.ConfirmedConsolidation)
            return new(GameAuthStates.Pending, bound.State == "committing" ? GameAuthErrors.SteamLinkChangedStartAgain : GameAuthErrors.ConsolidationConfirmationRequired);
        Guid? mfaId = bound.ConfirmedMfaId;
        if (bound.State != "committing")
        {
            if (bound.State != "verified" || bound.ProofExpiresAt <= clock.GetUtcNow().UtcDateTime) return new(GameAuthStates.Pending, GameAuthErrors.InvalidLink);
            LinkReauthenticated proof = await recent.RequireAsync(auth.Account, request.CurrentPassword, request.MfaCode, SourceAddress, ct);
            if (proof.Error is not null) return new(GameAuthStates.Pending, proof.Error);
            mfaId = proof.ConfirmedMfaId;
        }
        SteamWebLinkRecord? record = await links.CommitAsync(request.TransactionId, auth.Account, BrowserSession, Cookie(request.TransactionId), requestId, mfaId, ct, request.ConfirmedConsolidation);
        if (record?.SteamSubject is null) return new(GameAuthStates.Pending, GameAuthErrors.InvalidLink);
        // Recover a durable authorization even if its short provider proof expired after the SQL commit.
        AccountConsolidation? existing = await operations.FindAsync(record.Id, ct);
        if (existing is not null) return new("consolidating") { Consolidation = await consolidation.ResumeAsync(record.Id, auth.Account.Id, ct) };
        ExternalIdentity? identity = await identities.FindAsync(StoreProviders.Steam, record.SteamSubject, ct);
        if (identity?.AccountId == record.AccountId) return new("linked");
        if (identity is not null && identity.AccountId != record.AccountId)
        {
            if (!record.ConsolidationConsent) return new(GameAuthStates.Pending, GameAuthErrors.SteamLinkChangedStartAgain);
            AccountConsolidationReply reply = await consolidation.BeginAsync(new(record.Id, record.AccountId, record.SteamSubject, record.CredentialsVersion,
                record.SessionEpoch, record.ConfirmedMfaId, [], record.ProofExpiresAt!.Value), ct);
            return new("consolidating", reply.Error) { Consolidation = reply };
        }
        IdentityLinkResult link = await identities.LinkWithAuthorityAsync(new(record.Id, record.AccountId, StoreProviders.Steam, record.SteamSubject,
            record.CredentialsVersion, record.SessionEpoch, record.ConfirmedMfaId)
        { ProofExpiresAt = record.ProofExpiresAt!.Value }, clock.GetUtcNow().UtcDateTime, ct);
        return link.Status is IdentityLinkStatus.Linked or IdentityLinkStatus.AlreadyLinked ? new("linked") :
            new(GameAuthStates.Pending, link.Status == IdentityLinkStatus.SubjectTaken ? GameAuthErrors.SteamLinkChangedStartAgain : GameAuthErrors.LinkUnavailable);
    });

    [HttpGet("consolidations/pending", Name = "GetPendingAccountConsolidation")]
    [ProducesResponseType(typeof(SteamWebLinkReply), 200)]
    public Task<IActionResult> Pending(CancellationToken ct) => Execute(async () =>
    {
        AccountConsolidation? operation = await operations.FindPendingForTargetAsync(auth.Account!.Id, ct);
        return operation is null ? new("none") : new("consolidating") { Consolidation = await consolidation.StatusAsync(operation.Id, auth.Account.Id, ct) };
    });
    [HttpGet("consolidations/{id:guid}", Name = "GetAccountConsolidation")]
    [ProducesResponseType(typeof(SteamWebLinkReply), 200)]
    public Task<IActionResult> Status(Guid id, CancellationToken ct) => Execute(async () =>
    {
        AccountConsolidationReply reply = await consolidation.StatusAsync(id, auth.Account!.Id, ct);
        return new("consolidating", reply.Error) { Consolidation = reply };
    });
    [HttpPost("consolidations/{id:guid}/resume", Name = "ResumeAccountConsolidation")]
    [ProducesResponseType(typeof(SteamWebLinkReply), 200)]
    public Task<IActionResult> Resume(Guid id, CancellationToken ct) => Execute(async () =>
    {
        AccountConsolidationReply reply = await consolidation.ResumeAsync(id, auth.Account!.Id, ct);
        return new("consolidating", reply.Error) { Consolidation = reply };
    });
    private async Task<IActionResult> Execute(Func<Task<SteamWebLinkReply>> action)
    {
        if (!Browser) return Unauthorized();
        try { SteamWebLinkReply reply = await action(); return reply.Error is null ? Ok(reply) : BadRequest(reply); }
        catch (Exception e) when (e is RedisException or JsonException or CryptographicException or DbException)
        { return StatusCode(503, new SteamWebLinkReply(GameAuthStates.Pending, GameAuthErrors.ServiceUnavailable)); }
    }
}
