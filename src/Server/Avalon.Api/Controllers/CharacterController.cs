using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Contract.Mappers;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

[Authorize]
[ApiController]
[Route("character")]
public class CharacterController : BaseController
{
    private readonly IAuthContext _authContext;
    private readonly ICharacterService _service;

    public CharacterController(IAuthContext authContext, ICharacterService service)
    {
        _authContext = authContext;
        _service = service;
    }

    [HttpGet(Name = "GetCharacter")]
    public async Task<IList<CharacterDto>> GetAll()
    {
        var characters = await _service.GetAllCharacters(_authContext.Account?.Id ?? throw new Exception("Account not loaded"));
        return characters.Select(c => c.ToDto()).ToList();
    }

    [HttpGet("{id}", Name = "GetCharacterById")]
    [ProducesResponseType(typeof(CharacterDto), 200)]
    public async Task<CharacterDto> GetById([FromRoute] uint id)
    {
        var character = await _service.GetCharacterById(id);
        return character.ToDto();
    }

}
