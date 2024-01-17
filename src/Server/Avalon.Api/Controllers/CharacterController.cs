using Avalon.Api.Authentication;
using Avalon.Domain.Characters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

[Authorize]
[ApiController]
[Route("character")]
public class CharacterController : BaseController
{
    private readonly IAuthContext _authContext;

    public CharacterController(IAuthContext authContext)
    {
        _authContext = authContext;
    }
    
    /*
    [HttpGet(Name = "GetCharacter")]
    public async Task<Character> GetAll()
    {
        return await Task.FromResult(_authContext.Character ?? throw new Exception("Character not loaded"));
    }
    */
}
