using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Database.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class AccountController : BaseController
{
    private readonly IAccountService _accountService;
    
    public AccountController(IAccountService accountService)
    {
        _accountService = accountService;
    }
    
    [Authentication.Authorize]
    [HttpGet(Name = "GetAccount")]
    public async Task<Account> Get()
    {
        return Account ?? throw new UnauthorizedAccessException();
    }
    
    [AllowAnonymous]
    [HttpPost("authenticate", Name = "Authenticate")]
    public async Task<IActionResult> Authenticate([FromBody] AuthenticateRequest model)
    {
        var jwt = await _accountService.Authenticate(model, IpAddress, CancellationToken);
        if (jwt == null)
            return BadRequest(new {message = "Username or password is incorrect"});
        return Ok(jwt);
    }
    
    [AllowAnonymous]
    [HttpPost("register", Name = "Register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest model)
    {
        var jwt = await _accountService.Register(model, IpAddress, CancellationToken);
        return Ok(jwt);
    }
}
