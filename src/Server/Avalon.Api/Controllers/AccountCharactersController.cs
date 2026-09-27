using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

/// <summary>The caller's characters on every world (#523). Everything else about characters is per world.</summary>
[Authorize(Policy = AvalonRoles.Player)]
[ApiController]
[Route("character")]
public class AccountCharactersController : BaseController
{
    private readonly IAuthContext _authContext;
    private readonly IAccountCharactersService _service;

    public AccountCharactersController(IAuthContext authContext, IAccountCharactersService service)
    {
        _authContext = authContext;
        _service = service;
    }

    [HttpGet(Name = "GetCharacters")]
    [ProducesResponseType(typeof(CharacterListDto), StatusCodes.Status200OK)]
    public Task<CharacterListDto> GetAll(CancellationToken ct)
    {
        var accountId = _authContext.Account?.Id
            ?? throw new InvalidOperationException("Account not loaded");

        return _service.GetAsync(accountId, User.AccessLevel(), ct);
    }
}
