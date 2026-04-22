using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Contract.Mappers;
using Avalon.Api.Services;
using Avalon.Database;
using Avalon.Database.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

[Authorize]
[ApiController]
[Route("account")]
public class AccountController : BaseController
{
    private readonly IAccountService _accountService;
    private readonly IAuthContext _authContext;

    public AccountController(IAccountService accountService, IAuthContext authContext)
    {
        _accountService = accountService;
        _authContext = authContext;
    }

    [HttpGet(Name = "GetAccount")]
    [ProducesResponseType(typeof(AccountDto), 200)]
    public async Task<AccountDto> Get()
    {
        var dto = (_authContext.Account ?? throw new Exception("Account not loaded")).ToDto();
        return await Task.FromResult(dto);
    }

    [Authorize(Policy = AvalonRoles.GameMaster)]
    [HttpGet("{id:long}", Name = "FindAccountById")]
    [ProducesResponseType(typeof(AccountDto), 200)]
    public async Task<AccountDto> FindById([FromRoute] long id)
    {
        var account = await _accountService.FindById(id);
        if (account is null)
        {
            throw new Exception("Account not found");
        }
        return account.ToDto();
    }

    [Authorize(Policy = AvalonRoles.GameMaster)]
    [HttpGet("paginate", Name = "PaginateAccounts")]
    [ProducesResponseType(typeof(PagedResult<AccountDto>), 200)]
    public async Task<PagedResult<AccountDto>> Paginate([FromQuery] AccountPaginateFilters filters)
    {
        var results = (await _accountService.Paginate(filters, HttpContext.RequestAborted)).MapTo(x => x.ToDto());
        return results;
    }

    [AllowAnonymous]
    [HttpPost("authenticate", Name = "Authenticate")]
    public async Task<AuthenticateResponse> Authenticate([FromBody] AuthenticateRequest model)
    {
        return await _accountService.Authenticate(model, IpAddress, CancellationToken);
    }

    [AllowAnonymous]
    [HttpPost("register", Name = "Register")]
    public async Task<RegisterResponse> Register([FromBody] RegisterRequest model)
    {
        var userAgent = Request.Headers.UserAgent.ToString();
        var language = Request.Headers.AcceptLanguage.ToString();
        return await _accountService.Register(model, userAgent, IpAddress, CancellationToken);
    }
}
