using Avalon.Api.Commerce;
using Avalon.Api.Contract.Commerce;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Controllers;
using Avalon.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

[Authorize(Policy = AvalonRoles.Admin)]
[ApiController]
[Route("admin/purchases")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminPurchasesController(IPurchaseAdministrationService purchases, IAuthContext context) : BaseController
{
    [HttpGet(Name = "SearchPurchases")]
    [ProducesResponseType(typeof(PagedResult<AdminPurchaseDto>), StatusCodes.Status200OK)]
    public Task<PagedResult<AdminPurchaseDto>> Search([FromQuery] PurchaseSearch search) => purchases.SearchAsync(search, CancellationToken);
    [HttpGet("{id:guid}", Name = "GetPurchaseDetails")]
    [ProducesResponseType(typeof(AdminPurchaseDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<AdminPurchaseDetailDto> Get([FromRoute] Guid id) => purchases.GetAsync(id, CancellationToken);
    [HttpPost("{id:guid}/refund", Name = "RequestPurchaseFullRefund")]
    [ProducesResponseType(typeof(RefundReply), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status501NotImplemented)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Refund([FromRoute] Guid id, [FromBody] FullRefundRequest request) =>
        Accepted(await purchases.RequestFullRefundAsync(context.Account!.Id, id, request.PaymentAttemptId, request.Reason, CancellationToken));
    [HttpPost("{id:guid}/retry", Name = "RetryPurchaseReconciliation")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Retry([FromRoute] Guid id)
    { await purchases.RetryReconciliationAsync(context.Account!.Id, id, CancellationToken); return Accepted(); }
}
