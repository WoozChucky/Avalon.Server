using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OtpNet;

namespace Avalon.Api.Controllers;

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
    
    [Authorize]
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
        return await _accountService.Register(model, IpAddress, CancellationToken);
    }
    
    [Authorize(AvalonRoles.Admin)]
    [HttpGet("test", Name = "Test")]
    public async Task<IActionResult> Test()
    {
        var totp = new Totp("base32secret3232"u8.ToArray());
        var uriString = new OtpUri(OtpType.Totp, "base32secret3232", "alice@google.com", "Avalon").ToString();        
        return Ok(new
        {
            code = totp.ComputeTotp(),
            uri = uriString
        });
    }
}
