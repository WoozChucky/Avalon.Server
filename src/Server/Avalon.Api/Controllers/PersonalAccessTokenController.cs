using System.Security.Claims;
using Avalon.Api.Authentication;
using Avalon.Api.Authorization;
using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

[ApiController]
[Authorize(Policy = AvalonRoles.Player)]
[Route("pat")]
public class PersonalAccessTokenController : BaseController
{
    private readonly IPersonalAccessTokenService _service;
    private readonly IAuthorizationService _authz;

    public PersonalAccessTokenController(IPersonalAccessTokenService service, IAuthorizationService authz)
    {
        _service = service;
        _authz = authz;
    }

    private bool CallerIsPat => User.HasClaim(c => c.Type == "pat_id");

    private static PatDto ToDto(PersonalAccessToken p) => new()
    {
        Id = p.Id.Value,
        AccountId = p.AccountId.Value,
        Name = p.Name,
        Prefix = p.TokenPrefix,
        Roles = (Avalon.Api.Contract.AccountAccessLevel)p.Roles,
        CreatedAt = p.CreatedAt,
        ExpiresAt = p.ExpiresAt,
        LastUsedAt = p.LastUsedAt,
        RevokedAt = p.RevokedAt,
    };

    private static Avalon.Domain.Auth.AccountAccessLevel CollectRoles(ClaimsPrincipal user)
    {
        var roles = (Avalon.Domain.Auth.AccountAccessLevel)0;
        foreach (Avalon.Domain.Auth.AccountAccessLevel flag in Enum.GetValues<Avalon.Domain.Auth.AccountAccessLevel>())
            if (flag != 0 && user.IsInRole(flag.ToString())) roles |= flag;
        return roles;
    }

    // ---------------- Self-service ----------------

    [HttpPost]
    [ProducesResponseType(typeof(PatCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create([FromBody] CreatePatRequest req, CancellationToken ct)
    {
        if (CallerIsPat)
            return StatusCode(StatusCodes.Status403Forbidden, "PAT cannot mint new PATs");

        var result = await _service.MintSelfAsync(
            callerId: User.AccountId(),
            callerRoles: CollectRoles(User),
            name: req.Name,
            expiresAt: req.ExpiresAt,
            requestedRoles: (Avalon.Domain.Auth.AccountAccessLevel?)req.Roles,
            ct);

        return CreatedAtAction(nameof(GetById), new { id = result.Id.Value }, new PatCreatedDto
        {
            Id = result.Id.Value,
            AccountId = User.AccountId().Value,
            Name = result.Name,
            Prefix = result.Prefix,
            Roles = (Avalon.Api.Contract.AccountAccessLevel)result.Roles,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = result.ExpiresAt,
            Token = result.Token,
        });
    }

    [HttpGet]
    public async Task<IList<PatDto>> List(CancellationToken ct)
    {
        var list = await _service.ListByAccountAsync(User.AccountId(), includeRevoked: true, ct);
        return list.Select(ToDto).ToList();
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(PatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] uint id, CancellationToken ct)
    {
        var pat = await _service.GetAsync(new PersonalAccessTokenId(id), ct);
        if (pat is null) return NotFound();

        var authz = await _authz.AuthorizeAsync(User, pat, new ReadRequirement());
        if (!authz.Succeeded) return NotFoundOrForbid();

        return Ok(ToDto(pat));
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Revoke([FromRoute] uint id, CancellationToken ct)
    {
        var pat = await _service.GetAsync(new PersonalAccessTokenId(id), ct);
        if (pat is null) return NotFound();

        var authz = await _authz.AuthorizeAsync(User, pat, new WriteRequirement());
        if (!authz.Succeeded) return NotFoundOrForbid();

        await _service.RevokeAsync(pat, User.AccountId(), ct);
        return NoContent();
    }

    // ---------------- Admin ----------------

    [HttpPost("admin")]
    [Authorize(Policy = AvalonRoles.Admin)]
    [ProducesResponseType(typeof(PatCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateAdmin([FromBody] CreateAdminPatRequest req, CancellationToken ct)
    {
        if (CallerIsPat)
            return StatusCode(StatusCodes.Status403Forbidden, "PAT cannot mint new PATs");

        var result = await _service.MintAdminAsync(
            callerRoles: CollectRoles(User),
            targetAccountId: new AccountId(req.AccountId),
            name: req.Name,
            expiresAt: req.ExpiresAt,
            requestedRoles: (Avalon.Domain.Auth.AccountAccessLevel)req.Roles,
            ct);

        return CreatedAtAction(nameof(GetById), new { id = result.Id.Value }, new PatCreatedDto
        {
            Id = result.Id.Value,
            AccountId = req.AccountId,
            Name = result.Name,
            Prefix = result.Prefix,
            Roles = (Avalon.Api.Contract.AccountAccessLevel)result.Roles,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = result.ExpiresAt,
            Token = result.Token,
        });
    }

    [HttpGet("admin")]
    [Authorize(Policy = AvalonRoles.Admin)]
    public async Task<IList<PatDto>> ListAdmin(
        [FromQuery] long accountId,
        [FromQuery] bool includeRevoked = false,
        CancellationToken ct = default)
    {
        var list = await _service.ListByAccountAsync(new AccountId(accountId), includeRevoked, ct);
        return list.Select(ToDto).ToList();
    }

    [HttpDelete("admin/account/{accountId:long}")]
    [Authorize(Policy = AvalonRoles.Admin)]
    public async Task<IActionResult> RevokeAllForAccount([FromRoute] long accountId, CancellationToken ct)
    {
        var count = await _service.RevokeAllForAccountAsync(new AccountId(accountId), User.AccountId(), ct);
        return Ok(new { revoked = count });
    }
}
