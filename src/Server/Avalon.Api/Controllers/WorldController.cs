using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

[ApiController]
[Authorize(Policy = AvalonRoles.Player)]
[Route("world")]
public class WorldController : BaseController
{
    private readonly IWorldService _service;

    public WorldController(IWorldService service)
    {
        _service = service;
    }

    [HttpGet(Name = "ListWorlds")]
    [ProducesResponseType(typeof(PagedResult<WorldDto>), StatusCodes.Status200OK)]
    public Task<PagedResult<WorldDto>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        _service.ListAsync(page, pageSize, ct);

    [HttpGet("{id}", Name = "GetWorldById")]
    [ProducesResponseType(typeof(WorldDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] ushort id, CancellationToken ct)
    {
        var world = await _service.GetAsync(id, ct);
        return world is null ? NotFound() : Ok(world);
    }

    [HttpPost(Name = "CreateWorld")]
    [Authorize(Policy = AvalonRoles.Admin)]
    [ProducesResponseType(typeof(WorldDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateWorldRequest request, CancellationToken ct)
    {
        var world = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = world.Id }, world);
    }

    [HttpPatch("{id}", Name = "UpdateWorld")]
    [Authorize(Policy = AvalonRoles.Admin)]
    [ProducesResponseType(typeof(WorldDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        [FromRoute] ushort id,
        [FromBody] UpdateWorldRequest request,
        CancellationToken ct)
    {
        var world = await _service.UpdateAsync(id, request, ct);
        return world is null ? NotFound() : Ok(world);
    }
}
