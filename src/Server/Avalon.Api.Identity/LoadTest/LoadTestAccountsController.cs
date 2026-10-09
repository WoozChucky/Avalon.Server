using Avalon.Api.Contract;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Controllers;
using Avalon.Api.Identity.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Identity.LoadTest;

/// <summary>
/// Load-test bot accounts, for admins. Always mapped; while <c>Application:LoadTest:Enabled</c> is false every action
/// answers an empty 404, whatever the body. A personal access token is refused, and the admin's own current password
/// is required, as for <c>POST /pat/admin</c>: these accounts can enter PTR worlds.
/// </summary>
[ApiController]
[Authorize(Policy = AvalonRoles.Admin)]
[Route("admin/load-test/accounts")]
public sealed class LoadTestAccountsController(ILoadTestAccounts accounts, IReauthentication reauthentication,
    IOptions<LoadTestOptions> options) : BaseController, IActionFilter
{
    private bool CallerIsPat => User.HasClaim(c => c.Type == "pat_id");

    /// <summary>
    /// The 404 while disabled. A controller's own filter runs before every other action filter, the automatic 400 for
    /// an invalid body included, so a disabled endpoint says nothing about what it would accept.
    /// </summary>
    [NonAction]
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!options.Value.Enabled)
            context.Result = NotFound();
    }

    [NonAction]
    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    [HttpPost]
    [ProducesResponseType(typeof(LoadTestRunCreated), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateLoadTestRunRequest req, CancellationToken ct)
    {
        if (CallerIsPat)
            return StatusCode(StatusCodes.Status403Forbidden, "A personal access token cannot create load-test accounts");

        await reauthentication.RequireCurrentPasswordAsync(User.AccountId(), req.CurrentPassword, SourceAddress, ct);

        LoadTestRunCreated created = await accounts.CreateAsync(User.AccountId(), req, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }
}
