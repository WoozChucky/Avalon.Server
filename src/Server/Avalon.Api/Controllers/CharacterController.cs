using AutoMapper;
using Avalon.Api.Authentication;
using Avalon.Api.Contract;
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
    private readonly IMapper _mapper;

    public CharacterController(IAuthContext authContext, ICharacterService service, IMapper mapper)
    {
        _authContext = authContext;
        _service = service;
        _mapper = mapper;
    }
    
    [HttpGet(Name = "GetCharacter")]
    public async Task<IList<CharacterDto>> GetAll()
    {
        var characters = await _service.GetAllCharacters(_authContext.Account?.Id ?? throw new Exception("Account not loaded"));
        var mapped = _mapper.Map<IList<CharacterDto>>(characters);
        return mapped;
    }
    
    [HttpGet("{id}", Name = "GetCharacterById")]
    public async Task<CharacterDto> GetById(CharacterId id)
    {
        var character = await _service.GetCharacterById(id);
        var mapped = _mapper.Map<CharacterDto>(character);
        return mapped;
    }
    
}
