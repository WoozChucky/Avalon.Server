using Avalon.Api.Authentication;
using Avalon.Api.Commerce;
using Avalon.Api.Contract.Commerce;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Login;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

[Authorize(Policy = AvalonRoles.Player)]
[ApiController]
[Route("account")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountPurchasesController(IPurchaseService purchases, IAuthContext context) : BaseController
{
    [HttpGet("game-license", Name = "GetAccountGameLicense")]
    [ProducesResponseType(typeof(AccountGameLicenseDto), StatusCodes.Status200OK)]
    public Task<AccountGameLicenseDto> License() => purchases.GetLicenseStatusAsync(context.Account!.Id, CancellationToken);

    [HttpGet("purchases/{id:guid}", Name = "GetAccountPurchase")]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<PurchaseOrderDto> Get([FromRoute] Guid id) => purchases.GetOrderAsync(context.Account!.Id, id, CancellationToken);

    [HttpPost("purchases/checkout", Name = "CreateAccountPurchaseCheckout")]
    [ProducesResponseType(typeof(CheckoutReply), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CheckoutReply), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status501NotImplemented)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Checkout()
    {
        var account = context.Account!;
        var reply = await purchases.CreateCheckoutAsync(account.Id, account.CredentialsVersion, RemoteAddress.SourceOf(SourceAddress), CancellationToken);
        return reply.CheckoutUrl is null ? Accepted(reply) : Ok(reply);
    }
}
