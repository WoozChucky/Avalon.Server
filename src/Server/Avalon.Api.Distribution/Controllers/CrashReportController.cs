using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Avalon.Api.Distribution.Controllers;

[ApiController]
[Route("client/crash")]
public sealed class CrashReportController(CrashReportService service) : ControllerBase
{
    /// <summary>Grant an anonymous, size-bound crash ZIP upload to private storage for ten minutes.</summary>
    [HttpPost(Name = "CreateCrashReportUpload")]
    [AllowAnonymous]
    [EnableRateLimiting(CrashReportRateLimiting.Policy)]
    [RequestSizeLimit(1024)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(CrashReportSlot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create(CrashReportRequest request, CancellationToken ct) =>
        await service.CreateAsync(request, ct) is { } slot ? Ok(slot) : BadRequest();
}
