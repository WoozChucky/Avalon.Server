using Avalon.Api.Authentication;
using Avalon.Api.Authorization;
using Avalon.Api.Contract;
using Avalon.Api.Contract.Mappers;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

[Authorize(Policy = AvalonRoles.Player)]
[ApiController]
[Route("account")]
public class AccountController : BaseController
{
    private readonly IAccountService _accountService;
    private readonly IAuthContext _authContext;
    private readonly IAuthorizationService _authz;

    public AccountController(IAccountService accountService, IAuthContext authContext, IAuthorizationService authz)
    {
        _accountService = accountService;
        _authContext = authContext;
        _authz = authz;
    }

    [HttpGet(Name = "GetAccount")]
    [ProducesResponseType(typeof(AccountDto), 200)]
    public async Task<AccountDto> Get()
    {
        var dto = (_authContext.Account ?? throw new Exception("Account not loaded")).ToDto();
        return await Task.FromResult(dto);
    }

    [HttpGet("{id:long}", Name = "FindAccountById")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> FindById([FromRoute] long id, CancellationToken ct)
    {
        var account = await _accountService.FindByIdAsync(new AccountId(id), ct);
        if (account is null) return NotFound();

        var authz = await _authz.AuthorizeAsync(User, account, new ReadRequirement());
        if (!authz.Succeeded) return NotFoundOrForbid();

        return Ok(account.ToDto());
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

    [HttpPost("password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] AccountPasswordChangeRequest request, CancellationToken ct)
    {
        var accountId = User.AccountId();
        await _accountService.ChangePasswordAsync(accountId, request.CurrentPassword, request.NewPassword, ct);
        return NoContent();
    }

    [HttpPost("email/change")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> InitiateEmailChange([FromBody] AccountEmailChangeRequest req, CancellationToken ct)
    {
        await _accountService.InitiateEmailChangeAsync(User.AccountId(), req.NewEmail, ct);
        return Accepted();
    }

    [AllowAnonymous]
    [HttpPost("email/confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmEmailChange([FromBody] AccountEmailConfirmRequest req, CancellationToken ct)
    {
        await _accountService.ConfirmEmailChangeAsync(req.Token, ct);
        return NoContent();
    }
}
