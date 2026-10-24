using Avalon.Api.Contract.Crashes;
using Avalon.Crashes.Queries;
using Avalon.Crashes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Identity.Crashes;

[ApiController]
[Authorize(Policy = CrashAdminPolicy.Name)]
[Route("admin/crashes/reports")]
[ServiceFilter<CrashFeatureGate>]
public sealed class CrashReportsController(ICrashReadStore reads, TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("{id}", Name = "GetCrashReport")]
    [EndpointDescription("Get a bounded retained report with named analysis, thread, frame and module schemas and nullable logTail. Lifecycle status values are pending, processing, retry_wait, processed, rejected and failed. Expired and nonexistent IDs both return 404 CRASH_REPORT_NOT_AVAILABLE: This report has expired or is unavailable. There is no raw-download endpoint.")]
    [ProducesResponseType<CrashReportDto>(200)]
    [ProducesResponseType<ProblemDetails>(400, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(403)]
    [ProducesResponseType<ProblemDetails>(404, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    public async Task<IActionResult> GetCrashReport(Guid id, CancellationToken ct)
    {
        if (Request.Query.Count != 0)
            return CrashProblemMapper.Invalid();
        DateTimeOffset now = timeProvider.GetUtcNow().ToUniversalTime();
        CrashReportDetail? report = await reads.GetReportAsync(new CrashReportId(id), now, ct);
        return report is null || report.ExpiresAt <= now ? CrashProblemMapper.MissingReport() : Ok(CrashDtoMapper.Report(report));
    }
}
