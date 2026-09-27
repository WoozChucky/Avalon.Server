using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Api.Worlds;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

/// <summary>
/// One world's live presence for GameMasters (#556). Character ids are unique only within one world,
/// so a character's presence is asked for under the world it lives on, behind the #523 world check
/// (<see cref="WorldRouteMiddleware"/>: 404 for a world unknown or not permitted, 503 for one
/// unavailable), and only that world's presence key is read. The cross-world views stay on
/// <see cref="ObservabilityController"/>.
/// </summary>
[Authorize(Policy = AvalonRoles.GameMaster)]
[ApiController]
[WorldScoped]
[Route("world/{worldId:int}/observability")]
public class WorldObservabilityController : BaseController
{
    private readonly IObservabilityService _service;
    private readonly ICurrentWorld _world;

    public WorldObservabilityController(IObservabilityService service, ICurrentWorld world)
    {
        _service = service;
        _world = world;
    }

    /// <summary>
    /// 404 means "not currently in this world", which is a different answer from "no such
    /// character". The SPA renders an offline empty state for it rather than an error.
    /// </summary>
    [HttpGet("character/{id}", Name = "GetPlayerPresence")]
    [ProducesResponseType(typeof(PlayerPresenceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlayerPresence([FromRoute] uint id, CancellationToken ct)
    {
        WorldId world = _world.Id ?? throw new InvalidOperationException("No world selected for this request.");
        PlayerPresenceDto? presence = await _service.GetPlayerPresenceAsync(world, id, User.AccessLevel(), ct);
        return presence is null ? NotFound() : Ok(presence);
    }
}
