using Avalon.Api.Authentication;
using Avalon.Api.Authorization;
using Avalon.Api.Contract;
using Avalon.Api.Contract.Mappers;
using Avalon.Api.Services;
using Avalon.Api.Worlds;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Extensions;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

/// <summary>One world's characters (#523). The caller's characters on every world: GET /character.</summary>
[Authorize(Policy = AvalonRoles.Player)]
[ApiController]
[WorldScoped]
[Route("world/{worldId:int}/character")]
public class CharacterController : BaseController
{
    private readonly ICharacterService _service;
    private readonly IAuthorizationService _authz;
    private readonly ICurrentWorld _world;

    public CharacterController(ICharacterService service, IAuthorizationService authz, ICurrentWorld world)
    {
        _service = service;
        _authz = authz;
        _world = world;
    }

    [Authorize(Policy = AvalonRoles.GameMaster)]
    [HttpGet("paginate", Name = "PaginateCharacters")]
    [ProducesResponseType(typeof(PagedResult<CharacterDto>), 200)]
    public async Task<PagedResult<CharacterDto>> Paginate([FromQuery] CharacterPaginateFilters filters, CancellationToken ct)
    {
        var page = await _service.PaginateAsync(filters, ct);
        return page.MapTo(ToDto);
    }

    [HttpGet("{id}", Name = "GetCharacterById")]
    [ProducesResponseType(typeof(CharacterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetById([FromRoute] uint id, CancellationToken ct)
    {
        var character = await _service.GetCharacterByIdAsync(new CharacterId(id), ct);
        if (character is null) return NotFound();

        var authz = await _authz.AuthorizeAsync(User, character, new ReadRequirement());
        if (!authz.Succeeded) return NotFoundOrForbid();

        return Ok(ToDto(character));
    }

    [HttpGet("{id}/inventory", Name = "GetCharacterInventory")]
    [ProducesResponseType(typeof(CharacterInventoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInventory([FromRoute] uint id, CancellationToken ct)
    {
        var character = await _service.GetCharacterByIdAsync(new CharacterId(id), ct);
        if (character is null) return NotFound();

        var authz = await _authz.AuthorizeAsync(User, character, new ReadRequirement());
        if (!authz.Succeeded) return NotFoundOrForbid();

        var inventory = await _service.GetInventoryAsync(new CharacterId(id), ct);
        return Ok(inventory);
    }

    [HttpGet("{id}/abilities", Name = "GetCharacterAbilities")]
    [ProducesResponseType(typeof(CharacterAbilitiesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAbilities([FromRoute] uint id, CancellationToken ct)
    {
        var character = await _service.GetCharacterByIdAsync(new CharacterId(id), ct);
        if (character is null) return NotFound();

        var authz = await _authz.AuthorizeAsync(User, character, new ReadRequirement());
        if (!authz.Succeeded) return NotFoundOrForbid();

        var abilities = await _service.GetAbilitiesAsync(new CharacterId(id), ct);
        return Ok(abilities);
    }

    [HttpPatch("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Patch([FromRoute] uint id, [FromBody] CharacterPatchDto dto, CancellationToken ct)
    {
        var character = await _service.GetCharacterByIdAsync(new CharacterId(id), ct);
        if (character is null) return NotFound();

        var authz = await _authz.AuthorizeAsync(User, character, new WriteRequirement());
        if (!authz.Succeeded) return NotFoundOrForbid();

        if (User.HasRoleAtLeast(AvalonRoles.Admin))
            await _service.UpdateAnyAsync(character, dto, ct);
        else
            await _service.UpdateCosmeticAsync(character, dto.Name, ct);

        return NoContent();
    }

    private CharacterDto ToDto(Character character)
    {
        WorldId world = _world.Id ?? throw new InvalidOperationException("No world selected for this request.");
        return character.ToDto(world.Value, _world.Name);
    }
}
