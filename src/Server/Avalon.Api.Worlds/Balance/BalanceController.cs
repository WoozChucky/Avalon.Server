using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Controllers;
using Avalon.Balance.Contract;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Worlds.Balance;

/// <summary>
/// The balance workbench: admin-only calls forwarded to the in-cluster balance service. A status the
/// service answers with, and its JSON body, reach the caller unchanged, except 401, which is never
/// forwarded (see <see cref="BalanceClient" />).
/// </summary>
[ApiController]
[Authorize(Policy = AvalonRoles.Admin)]
[Route("balance")]
public class BalanceController : BaseController
{
    private readonly IBalanceClient _client;

    public BalanceController(IBalanceClient client)
    {
        _client = client;
    }

    /// <summary>The tunables, the default config and the lists a run can be filtered by.</summary>
    [HttpGet("catalog", Name = "GetBalanceCatalog")]
    [ProducesResponseType(typeof(CatalogDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Catalog(CancellationToken ct) => Forward(await _client.CatalogAsync(ct));

    /// <summary>Queues a simulation run.</summary>
    [HttpPost("runs", Name = "StartBalanceRun")]
    [ProducesResponseType(typeof(RunAcceptedDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(RunStatusDto), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> StartRun([FromBody] RunRequestDto request, CancellationToken ct) =>
        Forward(await _client.StartRunAsync(request, ct));

    /// <summary>A run's progress and, once done, its result.</summary>
    [HttpGet("runs/{id}", Name = "GetBalanceRun")]
    [ProducesResponseType(typeof(RunStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetRun(string id, CancellationToken ct) => Forward(await _client.GetRunAsync(id, ct));

    /// <summary>Cancels a queued or running run.</summary>
    [HttpDelete("runs/{id}", Name = "CancelBalanceRun")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CancelRun(string id, CancellationToken ct) => Forward(await _client.CancelRunAsync(id, ct));

    /// <summary>Opens a draft pull request with the changes.</summary>
    [HttpPost("exports", Name = "ExportBalanceChanges")]
    [ProducesResponseType(typeof(ExportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RunStatusDto), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Export([FromBody] ExportRequestDto request, CancellationToken ct) =>
        Forward(await _client.ExportAsync(request, ct));

    // The service's JSON goes out as received, success included: re-serializing with the API's own
    // options would drop the nulls the wire format relies on. The typed DTOs only describe the OpenAPI.
    private IActionResult Forward(BalanceResponse response) =>
        response.Json is { } json
            ? new ContentResult { StatusCode = response.Status, Content = json, ContentType = "application/json" }
            : StatusCode(response.Status);
}
