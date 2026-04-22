using Avalon.Api.Authentication;
using Avalon.Api.Authorization;
using Avalon.Api.Contract;
using Avalon.Api.Contract.Mappers;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

[Authorize(Policy = AvalonRoles.Player)]
[ApiController]
[Route("character")]
public class CharacterController : BaseController
{
    private readonly IAuthContext _authContext;
    private readonly ICharacterService _service;
    private readonly IAuthorizationService _authz;

    public CharacterController(IAuthContext authContext, ICharacterService service, IAuthorizationService authz)
    {
        _authContext = authContext;
        _service = service;
        _authz = authz;
    }

    [HttpGet(Name = "GetCharacters")]
    public async Task<IList<CharacterDto>> GetAll(CancellationToken ct)
    {
        var accountId = _authContext.Account?.Id
            ?? throw new InvalidOperationException("Account not loaded");

        var characters = await _service.GetAllCharactersAsync(accountId, ct);
        return characters.Select(c => c.ToDto()).ToList();
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

        return Ok(character.ToDto());
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
}
