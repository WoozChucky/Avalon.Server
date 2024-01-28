using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Domain.Auth;
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
    public async Task<Account> Get()
    {
        return await Task.FromResult(_authContext.Account ?? throw new Exception("Account not loaded"));
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
