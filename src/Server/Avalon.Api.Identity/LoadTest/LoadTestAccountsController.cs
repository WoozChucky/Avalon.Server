using Avalon.Api.Contract;
using Avalon.Api.Hosting.Exceptions;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Controllers;
using Avalon.Api.Identity.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

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

    /// <summary>
    /// Deletes run <paramref name="run"/>'s accounts, or every load-test account with <paramref name="all"/>=true, with
    /// their characters in every world. Exactly one of the two, checked before the password: neither, both, or an empty
    /// or blank <c>run</c> is a 400, so a request built from an unset variable never deletes every run. 409 while any of
    /// them is in a game or has a character online, and then nothing is deleted; 409 too when a bot enters a game during
    /// the delete, after the run's characters were removed but before its accounts were: stop the bots and repeat it.
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(typeof(LoadTestRunDeleted), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete([FromQuery] string? run, [FromQuery] bool? all,
        [FromBody] DeleteLoadTestRunRequest req, CancellationToken ct)
    {
        if (CallerIsPat)
            return StatusCode(StatusCodes.Status403Forbidden, "A personal access token cannot delete load-test accounts");

        // Read from the query itself: binding turns an empty run= into null, which must not pass for "no run given".
        bool namesRun = Request.Query.TryGetValue("run", out StringValues runs);
        bool namesAll = Request.Query.ContainsKey("all");
        if (namesRun == namesAll)
            throw new BusinessException("Name the run to delete (run=<id>) or ask for every run (all=true): exactly one.");
        if (namesAll && all != true)
            throw new BusinessException("Every run is deleted with all=true only.");
        if (namesRun)
        {
            if (runs.Count != 1)
                throw new BusinessException("Name one run.");
            LoadTestAccountService.ParseRunScope(runs[0]);
        }

        await reauthentication.RequireCurrentPasswordAsync(User.AccountId(), req.CurrentPassword, SourceAddress, ct);

        return Ok(await accounts.DeleteAsync(User.AccountId(), namesRun ? runs[0] : null, namesAll, ct));
    }
}
