using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Database;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.WorldMaintenance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Avalon.Api.Controllers;

[ApiController]
[Authorize(Policy = AvalonRoles.Player)]
[Route("world")]
public class WorldController : BaseController
{
    private readonly IWorldService _service;
    private readonly IWorldMaintenanceRepository _maintenance;
    private readonly IWorldMaintenanceControl _control;
    private readonly IWorldReadiness _readiness;

    public WorldController(IWorldService service, IWorldMaintenanceRepository maintenance,
        IWorldMaintenanceControl control, IWorldReadiness readiness)
    {
        _service = service;
        _maintenance = maintenance;
        _control = control;
        _readiness = readiness;
    }

    /// <summary>The worlds the caller may enter, by the same rule as the TCP world list (#452).</summary>
    [HttpGet(Name = "ListWorlds")]
    [ProducesResponseType(typeof(PagedResult<WorldDto>), StatusCodes.Status200OK)]
    public Task<PagedResult<WorldDto>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Ascending) =>
        _service.ListAsync(User.AccessLevel(), page, pageSize, ct, sortBy, sortDirection);

    /// <summary>404 both for a missing world and for one the caller may not enter (#452).</summary>
    [HttpGet("{id}", Name = "GetWorldById")]
    [ProducesResponseType(typeof(WorldDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] ushort id, CancellationToken ct)
    {
        var world = await _service.GetAsync(id, User.AccessLevel(), ct);
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

    [HttpGet("{id}/maintenance", Name = "GetWorldMaintenance")]
    [Authorize(Policy = AvalonRoles.Admin)]
    [ProducesResponseType(typeof(WorldMaintenanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMaintenance([FromRoute] ushort id, CancellationToken ct)
    {
        var state = await _maintenance.ReadAsync(new WorldId(id), ct);
        if (state is null) return NotFound();
        bool ready = await _readiness.IsReadyAsync(id, ct);
        return Ok(WorldMaintenanceDto.From(state, ready));
    }

    [HttpPost("{id}/maintenance", Name = "EnableWorldMaintenance")]
    [Authorize(Policy = AvalonRoles.Admin)]
    [ProducesResponseType(typeof(WorldMaintenanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EnableMaintenance([FromRoute] ushort id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] WorldMaintenanceRequest? request,
        CancellationToken ct)
    {
        int graceMinutes = request?.GraceMinutes ?? 5;
        if (graceMinutes is < 1 or > 60) return BadRequest();
        var state = await _control.SetAsync(new WorldId(id), true,
            TimeSpan.FromMinutes(graceMinutes), $"account:{User.AccountId().Value}", ct);
        if (state is null) return NotFound();
        bool ready = await _readiness.IsReadyAsync(id, ct);
        return Ok(WorldMaintenanceDto.From(state, ready));
    }

    [HttpDelete("{id}/maintenance", Name = "DisableWorldMaintenance")]
    [Authorize(Policy = AvalonRoles.Admin)]
    [ProducesResponseType(typeof(WorldMaintenanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DisableMaintenance([FromRoute] ushort id, CancellationToken ct)
    {
        var state = await _control.SetAsync(new WorldId(id), false,
            TimeSpan.FromMinutes(5), $"account:{User.AccountId().Value}", ct);
        if (state is null) return NotFound();
        bool ready = await _readiness.IsReadyAsync(id, ct);
        return Ok(WorldMaintenanceDto.From(state, ready));
    }
}
