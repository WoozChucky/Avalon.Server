using Avalon.Api.Contract;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Controllers;
using Avalon.Api.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

[Authorize(Policy = AvalonRoles.Player)]
[ApiController]
[Route("account/email/verification")]
public sealed class AccountEmailVerificationController(IAccountEmailVerificationService service) : BaseController
{
    [HttpGet(Name = "GetAccountEmailVerificationStatus")]
    [ProducesResponseType(typeof(AccountEmailVerificationStatusDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(401)]
    public Task<AccountEmailVerificationStatusDto> Get(CancellationToken ct) => service.GetStatusAsync(User.AccountId(), ct);

    [HttpPost(Name = "RequestAccountEmailVerification")]
    [ProducesResponseType(202)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(typeof(ProblemDetails), 429)]
    [ProducesResponseType(typeof(ProblemDetails), 501)]
    [ProducesResponseType(typeof(ProblemDetails), 503)]
    public new async Task<IActionResult> Request(CancellationToken ct)
    {
        await service.RequestAsync(User.AccountId(), SourceAddress.ToString(), ct);
        return Accepted();
    }

    [HttpPost("confirm", Name = "ConfirmAccountEmailVerification")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Confirm([FromBody] AccountEmailVerificationConfirmRequest request, CancellationToken ct)
    {
        await service.ConfirmAsync(User.AccountId(), request.Token, ct);
        return NoContent();
    }
}
