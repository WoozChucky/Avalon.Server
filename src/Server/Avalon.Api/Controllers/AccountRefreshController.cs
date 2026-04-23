using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Api.Exceptions;
using Avalon.Api.Services;
using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("account/refresh")]
public sealed class AccountRefreshController : BaseController
{
    private readonly IRefreshTokenService _refresh;
    private readonly IJwtUtils _jwt;
    private readonly IAccountRepository _accounts;
    private readonly AuthenticationConfig _authConfig;
    private readonly IReplicatedCache _cache;

    public AccountRefreshController(
        IRefreshTokenService refresh,
        IJwtUtils jwt,
        IAccountRepository accounts,
        AuthenticationConfig authConfig,
        IReplicatedCache cache)
    {
        _refresh = refresh;
        _jwt = jwt;
        _accounts = accounts;
        _authConfig = authConfig;
        _cache = cache;
    }

    [HttpPost(Name = "RefreshAccessToken")]
    [ProducesResponseType(typeof(RefreshResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RefreshResponse>> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue(_authConfig.RefreshCookieName, out var raw) || string.IsNullOrEmpty(raw))
            return Unauthorized();

        try
        {
            var rotated = await _refresh.RotateAsync(raw, ct);
            var account = await _accounts.FindByIdAsync(rotated.AccountId, track: false, ct);
            if (account is null)
            {
                ClearRefreshCookie();
                return Unauthorized();
            }

            SetRefreshCookie(rotated.RawToken, rotated.ExpiresAt, _authConfig);

            return new RefreshResponse
            {
                Token = _jwt.GenerateJwtToken(account),
                ExpiresAt = DateTimeOffset.UtcNow
                    .AddMinutes(_authConfig.AccessTokenLifetimeMinutes)
                    .ToUnixTimeSeconds(),
            };
        }
        catch (RefreshTheftException ex)
        {
            await _cache.PublishAsync(CacheKeys.WorldAccountsDisconnectChannel, ex.AccountId.Value.ToString());
            ClearRefreshCookie();
            return Unauthorized();
        }
        catch (UnauthorizedAccessException)
        {
            ClearRefreshCookie();
            return Unauthorized();
        }
    }

    private void ClearRefreshCookie()
    {
        Response.Cookies.Delete(
            _authConfig.RefreshCookieName,
            new CookieOptions { Path = _authConfig.RefreshCookiePath });
    }
}
