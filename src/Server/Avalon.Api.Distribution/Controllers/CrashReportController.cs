using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Avalon.Api.Distribution.Controllers;

[ApiController]
[Route("client/crash")]
public sealed class CrashReportController(CrashReportService service, CrashNoticeService notices) : ControllerBase
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

    /// <summary>
    /// The client finished its upload (#1252): the slot's key goes on to the crash worker, which processes the report
    /// at once if it is there. 202 for any key a slot could have, whether or not the worker was reached.
    /// </summary>
    [HttpPost("uploaded", Name = "NoticeCrashReportUploaded")]
    [AllowAnonymous]
    [EnableRateLimiting(CrashReportRateLimiting.NoticePolicy)]
    [RequestSizeLimit(512)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Uploaded(CrashNoticeRequest request, CancellationToken ct) =>
        await notices.NoticeAsync(request.Key, ct) ? Accepted() : BadRequest();
}
