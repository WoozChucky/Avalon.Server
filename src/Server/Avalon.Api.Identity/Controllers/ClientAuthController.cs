using System.Globalization;
using System.Net;
using System.Security.Authentication;
using System.Security.Claims;
using Avalon.Api.Contract;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Controllers;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Identity.Authentication;
using Avalon.Api.Identity.Authentication.Jwt;
using Avalon.Api.Identity.Config;
using Avalon.Api.Identity.Exceptions;
using Avalon.Api.Identity.Services;
using Avalon.Common.Accounts;
using Avalon.Common.GameAuth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.GameTickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StackExchange.Redis;

namespace Avalon.Api.Identity.Controllers;

/// <summary>
/// Launcher sign-in (#591): OAuth 2.0 for native apps (RFC 8252) with PKCE, the browser half being the
/// website's <c>/launcher/authorize</c> page. The launcher never sees the password; its session is a
/// refresh-token family of its own (<see cref="SessionClient.Launcher"/>), separate from the website's,
/// and its tokens travel in bodies, never cookies. Every step is limited per source address on top of
/// the global limiter.
/// </summary>
[ApiController]
[Route("client/auth")]
// RFC 6749 §5.1: codes and tokens must not be kept by any cache (Cache-Control: no-store, Pragma: no-cache).
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ClientAuthController : BaseController
{
    private const string InvalidGrant = "invalid_grant";

    /// <summary>
    /// A personal access token is for scripts, with roles of its own: it may not open a launcher session,
    /// which would carry every role the account has and outlive the token (#591 review), nor list or end one.
    /// </summary>
    private bool CallerIsPat => User.HasClaim(c => c.Type == "pat_id");

    private readonly ILauncherAuthCodes _codes;
    private readonly IRefreshTokenService _refresh;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IReauthentication _reauthentication;
    private readonly IJwtUtils _jwt;
    private readonly IAccountRepository _accounts;
    private readonly AuthenticationConfig _authConfig;
    private readonly IReplicatedCache _cache;
    private readonly IGameTicketStore _tickets;
    private readonly Microsoft.Extensions.Options.IOptions<Avalon.Configuration.StoreAuthenticationConfiguration>? _storeOptions;
    private readonly TimeProvider _clock;
    private readonly Microsoft.AspNetCore.Builder.ForwardedHeadersOptions _forwarded;

    public ClientAuthController(
        ILauncherAuthCodes codes,
        IRefreshTokenService refresh,
        IRefreshTokenRepository refreshTokens,
        IReauthentication reauthentication,
        IJwtUtils jwt,
        IAccountRepository accounts,
        AuthenticationConfig authConfig,
        IReplicatedCache cache,
        IGameTicketStore tickets,
        Microsoft.AspNetCore.Builder.ForwardedHeadersOptions forwarded,
        Microsoft.Extensions.Options.IOptions<Avalon.Configuration.StoreAuthenticationConfiguration>? storeOptions = null,
        TimeProvider? clock = null)
    {
        _codes = codes;
        _refresh = refresh;
        _refreshTokens = refreshTokens;
        _reauthentication = reauthentication;
        _jwt = jwt;
        _accounts = accounts;
        _authConfig = authConfig;
        _cache = cache;
        _tickets = tickets;
        _storeOptions = storeOptions;
        _clock = clock ?? TimeProvider.System;
        _forwarded = forwarded;
    }

    /// <summary>
    /// Issues a one-time code, valid 60 seconds, for the signed-in account, bound to the launcher's PKCE
    /// challenge and loopback port and to the account's current credentials version.
    /// </summary>
    [HttpPost("code", Name = "CreateLauncherCode")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [Authorize(Policy = AvalonRoles.Player)]
    [EnableRateLimiting(ClientAuthRateLimiting.Policy)]
    [ProducesResponseType(typeof(ClientAuthCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Code([FromBody] ClientAuthCodeRequest request)
    {
        if (CallerIsPat) return PatRefused();
        Account account = Account ?? throw new InvalidOperationException("Account not loaded");
        // The current password, not just the session (#591 review): a stolen access token or refresh
        // cookie must not become a 30-day launcher session. A wrong one is 401, a spent budget 429 LOCKED.
        Reauthenticated proof = await _reauthentication.RequireCurrentPasswordAsync(account.Id, request.CurrentPassword,
            SourceAddress, CancellationToken);
        try
        {
            string code = await _codes.IssueAsync(account.Id, proof.CredentialsVersion, request.Challenge, request.RedirectPort);
            return Ok(new ClientAuthCodeResponse { Code = code });
        }
        catch (ArgumentException e)
        {
            return Problem(title: "Invalid launcher sign-in request", detail: e.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Trades a code and the verifier behind its challenge for the launcher's tokens.</summary>
    [HttpPost("token", Name = "ExchangeLauncherCode")]
    [AllowAnonymous]
    [EnableRateLimiting(ClientAuthRateLimiting.Policy)]
    [ProducesResponseType(typeof(ClientAuthTokens), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Token([FromBody] ClientAuthTokenRequest request)
    {
        LauncherGrant? grant = await _codes.RedeemAsync(request.Code, request.Verifier);
        // RFC 6749 §4.1.3: the exchange names the redirect the code was issued for.
        if (grant is null || grant.RedirectPort != request.RedirectPort) return InvalidGrantProblem();

        Account? account = await _accounts.FindByIdAsync(grant.AccountId, track: false, CancellationToken);
        // A ban, or a password or email change since the code was issued, voids it.
        if (!AccountAccessCheck.MayHoldSession(account) || account.CredentialsVersion != grant.CredentialsVersion)
            return InvalidGrantProblem();

        try
        {
            RefreshIssueResult issued = await _refresh.IssueLauncherAsync(account.Id, grant.CredentialsVersion,
                request.DeviceName, CancellationToken);
            return Ok(Tokens(account, issued.RawToken, issued.ExpiresAt, issued.FamilyId));
        }
        catch (AuthenticationException)
        {
            // The credentials moved between the check above and the insert.
            return InvalidGrantProblem();
        }
    }

    /// <summary>
    /// Rotates the launcher's refresh token. Presenting a rotated one again (outside the grace a lost
    /// answer gets) ends the session and the account's world sessions, as the website's refresh does.
    /// </summary>
    [HttpPost(GameAuthTokenKinds.Refresh, Name = "RefreshLauncherSession")]
    [AllowAnonymous]
    [EnableRateLimiting(ClientAuthRateLimiting.Policy)]
    [ProducesResponseType(typeof(ClientAuthTokens), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] ClientAuthRefreshRequest request)
    {
        if (string.IsNullOrEmpty(request.RefreshToken)) return Unauthorized();
        try
        {
            // As the website's refresh (#495 final review): behind a trusted proxy that forwarded no
            // client, the address is shared by everyone behind it, so the caller gets no source.
            IPAddress? peer = HttpContext.Connection.RemoteIpAddress;
            if (peer is not null && ForwardedHeadersSetup.IsTrustedProxy(_forwarded, peer))
                peer = null;
            var caller = RefreshCaller.From(peer, Request.Headers.UserAgent.ToString());
            RefreshRotateResult rotated = await _refresh.RotateLauncherAsync(request.RefreshToken, caller, CancellationToken);

            Account? account = await _accounts.FindByIdAsync(rotated.AccountId, track: false, CancellationToken);
            if (!AccountAccessCheck.MayHoldSession(account))
            {
                // No session outlives a ban (#480): the successor just minted goes with the rest.
                await _refresh.RevokeAllForAccountAsync(rotated.AccountId, CancellationToken);
                return Unauthorized();
            }

            // The credentials changed after the rotation committed (#495): that change revoked the successor.
            if (account.CredentialsVersion != rotated.CredentialsVersion) return Unauthorized();

            return Ok(Tokens(account, rotated.RawToken, rotated.ExpiresAt, rotated.FamilyId));
        }
        catch (RefreshAlreadyRotatedException)
        {
            return Unauthorized();
        }
        catch (RefreshTheftException ex)
        {
            await _cache.PublishAsync(CacheKeys.WorldAccountsDisconnectChannel, ex.AccountId.Value.ToString(CultureInfo.InvariantCulture));
            return Unauthorized();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    [HttpPost("game-ticket", Name = "CreateGameTicket")]
    [Authorize(Policy = AvalonRoles.Player)]
    [EnableRateLimiting(ClientAuthRateLimiting.Policy)]
    [ProducesResponseType(typeof(ClientGameTicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GameTicket()
    {
        if (CallerIsPat || !Guid.TryParse(User.FindFirstValue(JwtUtils.LauncherFamilyClaim), out Guid familyId)
            || familyId == Guid.Empty)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        Account account = Account ?? throw new InvalidOperationException("Account not loaded");
        if (!AccountAccessCheck.MayHoldSession(account) || !AccessLevels.Player.Allows(account.AccessLevel))
            return StatusCode(StatusCodes.Status403Forbidden);
        // A consolidating account keeps its website session (the website drives the consolidation), but no handoff: the
        // game could not redeem it until the consolidation finishes (#882). Checked here, not in MayHoldSession.
        if (account.GameplayConsolidationId is not null)
        {
            return Problem(title: "Account consolidating", detail: GameAuthErrors.AccountConsolidating,
                statusCode: StatusCodes.Status403Forbidden);
        }
        if (!await _refreshTokens.IsLiveLauncherFamilyAsync(account.Id, familyId, _clock.GetUtcNow().UtcDateTime, CancellationToken))
            return Unauthorized();
        long issues;
        try
        {
            issues = await _cache.IncrementAsync(CacheKeys.GameTicketIssueBudget(familyId), TimeSpan.FromMinutes(1));
        }
        catch (RedisException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        if (issues > 10)
            return StatusCode(StatusCodes.Status429TooManyRequests);
        string ticket;
        try
        {
            ticket = await _tickets.IssueAsync(
                new GameTicketGrant(account.Id, familyId, account.CredentialsVersion, account.SessionEpoch,
                    _storeOptions?.Value.Environment ?? "production"), CancellationToken);
        }
        catch (Exception exception) when (exception is RedisException or InvalidOperationException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        return Ok(new ClientGameTicketResponse
        {
            Ticket = ticket,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds(),
        });
    }

    /// <summary>Signs the launcher out: ends the session the token belongs to. 204 whatever the token.</summary>
    [HttpPost("revoke", Name = "RevokeLauncherSession")]
    [AllowAnonymous]
    [EnableRateLimiting(ClientAuthRateLimiting.Policy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke([FromBody] ClientAuthRefreshRequest request)
    {
        if (!string.IsNullOrEmpty(request.RefreshToken))
            await _refresh.RevokeLauncherSessionAsync(request.RefreshToken, CancellationToken);
        return NoContent();
    }

    /// <summary>The signed-in account's launcher sessions, most recently used first.</summary>
    [HttpGet("sessions", Name = "ListLauncherSessions")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [Authorize(Policy = AvalonRoles.Player)]
    [ProducesResponseType(typeof(IReadOnlyList<LauncherSessionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Sessions()
    {
        if (CallerIsPat) return PatRefused();
        Account account = Account ?? throw new InvalidOperationException("Account not loaded");
        IReadOnlyList<LiveFamily> families = await _refreshTokens.ListLiveFamiliesAsync(account.Id, SessionClient.Launcher,
            DateTime.UtcNow, CancellationToken);
        return Ok(families.Select(f => new LauncherSessionDto
        {
            Id = f.FamilyId,
            DeviceName = f.DeviceName,
            SignedInAt = f.SignedInAt,
            LastUsedAt = f.LastUsedAt,
            ExpiresAt = f.ExpiresAt,
        }).ToList());
    }

    /// <summary>Ends one of the signed-in account's launcher sessions. Another account's answers 404.</summary>
    [HttpDelete("sessions/{familyId:guid}", Name = "EndLauncherSession")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [Authorize(Policy = AvalonRoles.Player)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EndSession(Guid familyId)
    {
        if (CallerIsPat) return PatRefused();
        Account account = Account ?? throw new InvalidOperationException("Account not loaded");
        // Existence-hiding, as elsewhere: a session that is not the caller's is not found.
        if (!await _refreshTokens.IsLauncherFamilyOfAsync(account.Id, familyId, CancellationToken)) return NotFound();

        await _refreshTokens.RevokeFamilyAsync(familyId, CancellationToken);
        return NoContent();
    }

    private ClientAuthTokens Tokens(Account account, string refreshToken, DateTime refreshExpiresAt, Guid familyId) => new()
    {
        AccessToken = _jwt.GenerateLauncherJwtToken(account, familyId),
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_authConfig.AccessTokenLifetimeMinutes).ToUnixTimeSeconds(),
        RefreshToken = refreshToken,
        RefreshExpiresAt = new DateTimeOffset(DateTime.SpecifyKind(refreshExpiresAt, DateTimeKind.Utc)).ToUnixTimeSeconds(),
    };

    private ObjectResult PatRefused() =>
        Problem(title: "Not with a personal access token",
            detail: "Sign in to the launcher from the website, not with a personal access token.",
            statusCode: StatusCodes.Status403Forbidden);

    private ObjectResult InvalidGrantProblem() =>
        Problem(title: InvalidGrant, detail: "The sign-in code is not valid. Sign in again from the launcher.",
            statusCode: StatusCodes.Status400BadRequest);
}
