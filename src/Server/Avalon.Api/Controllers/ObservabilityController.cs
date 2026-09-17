using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

/// <summary>
/// Live player observability for GameMasters: where a player is right now, which map
/// instance and procedural seed they are standing in, and who shares that instance.
///
/// Reads only the presence snapshots world servers publish to Redis. It never talks to a
/// world server directly, so it adds no inbound surface to the game servers and returns
/// nothing at all when a world is down (rather than presenting stale data as live).
/// </summary>
[Authorize(Policy = AvalonRoles.GameMaster)]
[ApiController]
[Route("observability")]
public class ObservabilityController : BaseController
{
    private readonly IObservabilityService _service;

    public ObservabilityController(IObservabilityService service) => _service = service;

    [HttpGet("online", Name = "GetOnlinePlayers")]
    [ProducesResponseType(typeof(PagedResult<OnlinePlayerDto>), StatusCodes.Status200OK)]
    public Task<PagedResult<OnlinePlayerDto>> GetOnline(
        [FromQuery] PresencePaginateFilters filters, CancellationToken ct)
        => _service.GetOnlineAsync(filters, ct);

    /// <summary>
    /// 404 means "not currently in a world" — which is a different answer from
    /// "no such character". The SPA renders an offline empty state for it rather than
    /// an error.
    /// </summary>
    [HttpGet("character/{id}", Name = "GetPlayerPresence")]
    [ProducesResponseType(typeof(PlayerPresenceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlayerPresence([FromRoute] uint id, CancellationToken ct)
    {
        PlayerPresenceDto? presence = await _service.GetPlayerPresenceAsync(id, ct);
        return presence is null ? NotFound() : Ok(presence);
    }

    [HttpGet("instance/{instanceId:guid}", Name = "GetInstancePresence")]
    [ProducesResponseType(typeof(InstancePresenceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInstancePresence([FromRoute] Guid instanceId, CancellationToken ct)
    {
        InstancePresenceDto? instance = await _service.GetInstancePresenceAsync(instanceId, ct);
        return instance is null ? NotFound() : Ok(instance);
    }
}
