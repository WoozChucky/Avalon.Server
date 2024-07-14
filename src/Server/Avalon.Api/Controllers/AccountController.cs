using AutoMapper;
using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Services;
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
    private readonly IMapper _mapper;

    public AccountController(IAccountService accountService, IAuthContext authContext, IMapper mapper)
    {
        _accountService = accountService;
        _authContext = authContext;
        _mapper = mapper;
    }
    
    [HttpGet(Name = "GetAccount")]
    public async Task<AccountDto> Get()
    {
        var mapped = _mapper.Map<AccountDto>(_authContext.Account ?? throw new Exception("Account not loaded"));
        return await Task.FromResult(mapped);
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
        return await _accountService.Register(model, userAgent, IpAddress, CancellationToken);
    }
}
