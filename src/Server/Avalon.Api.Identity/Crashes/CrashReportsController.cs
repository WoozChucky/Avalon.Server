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
    [ProducesResponseType<CrashReportDto>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(403)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(503)]
    public async Task<IActionResult> GetCrashReport(Guid id, CancellationToken ct)
    {
        if (Request.Query.Count != 0)
            return CrashProblemMapper.Invalid();
        DateTimeOffset now = timeProvider.GetUtcNow().ToUniversalTime();
        CrashReportDetail? report = await reads.GetReportAsync(new CrashReportId(id), now, ct);
        return report is null || report.ExpiresAt <= now ? CrashProblemMapper.MissingReport() : Ok(CrashDtoMapper.Report(report));
    }
}
