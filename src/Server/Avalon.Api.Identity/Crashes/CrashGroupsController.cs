using System.Globalization;
using Avalon.Api.Contract.Crashes;
using Avalon.Crashes.Queries;
using Avalon.Crashes.Triage;
using Avalon.Crashes.Validation;
using Avalon.Crashes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Identity.Crashes;

[ApiController]
[Authorize(Policy = CrashAdminPolicy.Name)]
[Route("admin/crashes/groups")]
[ServiceFilter<CrashFeatureGate>]
[ProducesResponseType<ProblemDetails>(400, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
public sealed class CrashGroupsController(ICrashReadStore reads, ICrashTriageStore triage, TimeProvider timeProvider) : ControllerBase
{
    [HttpGet(Name = "ListCrashGroups")]
    [EndpointDescription("List crash groups, including groups with no retained reports when unfiltered. reportCount is the lifetime total and survives report expiry; retainedReportCount covers unexpired reports, matchingReportCount applies the version/since filters. dailyCounts contains 30 UTC date/count pairs for retained matching reports. asOf, retentionDays (90) and effectiveSince declare the retained window. Group status values are new, investigating, fixed and ignored. Pages are live views, not a frozen snapshot.")]
    [ProducesResponseType<CrashGroupPageDto>(200)]
    public async Task<IActionResult> ListCrashGroups([FromQuery] string? status, [FromQuery] string? version,
        [FromQuery] DateTimeOffset? since, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow().ToUniversalTime();
        if (!CrashQuery.IsAllowed(Request.Query, "status", "version", "since", "page", "pageSize")
            || !CrashQuery.Page(Request.Query["page"].FirstOrDefault(), page, 1000)
            || !CrashQuery.Page(Request.Query["pageSize"].FirstOrDefault(), pageSize, 100)
            || status is not null && CrashQuery.Status(status) is null
            || version is not null && !CrashObjectKey.IsVersion(version)
            || !CrashQuery.Since(Request.Query["since"].FirstOrDefault(), now))
        {
            return CrashProblemMapper.Invalid();
        }
        CrashGroupPage result = await reads.ListGroupsAsync(new CrashGroupFilter(CrashQuery.Status(status), version, since?.ToUniversalTime(), page, pageSize), now, ct);
        return Ok(CrashDtoMapper.Page(result));
    }

    [HttpGet("{id}", Name = "GetCrashGroup")]
    [EndpointDescription("Get long-lived group triage and the newest retained crashing-thread stack, independent of the reports page. Breakdowns and report summaries cover the unexpired window declared by asOf, windowStart, windowEnd and retainedReportCount; reportCount remains the lifetime total. When all reports expire, the group still returns 200 with latestReportId, latestStackReceivedAt, latestStack and latestStackQuality null, empty breakdowns and zero recent reports. Missing groups return 404 CRASH_GROUP_NOT_FOUND.")]
    [ProducesResponseType<CrashGroupDetailDto>(200)]
    [ProducesResponseType<ProblemDetails>(404, "application/problem+json")]
    public async Task<IActionResult> GetCrashGroup(Guid id, [FromQuery] int reportsPage = 1, [FromQuery] int reportsPageSize = 25, CancellationToken ct = default)
    {
        if (!CrashQuery.IsAllowed(Request.Query, "reportsPage", "reportsPageSize")
            || !CrashQuery.Page(Request.Query["reportsPage"].FirstOrDefault(), reportsPage, 1000)
            || !CrashQuery.Page(Request.Query["reportsPageSize"].FirstOrDefault(), reportsPageSize, 100))
        {
            return CrashProblemMapper.Invalid();
        }
        CrashGroupDetail? result = await reads.GetGroupAsync(new CrashGroupId(id), reportsPage, reportsPageSize, timeProvider.GetUtcNow().ToUniversalTime(), ct);
        return result is null ? CrashProblemMapper.MissingGroup() : Ok(CrashDtoMapper.Detail(result));
    }

    /// <summary>Update engineering triage. Notes must never contain logs, names, paths or report material.</summary>
    [HttpPatch("{id}", Name = "UpdateCrashGroup")]
    [EndpointDescription("Update long-lived engineering triage notes; never include logs, names, paths or report material. Omitted fields are unchanged; explicit null clears nullable fields. Requires triageRevision. Maximum JSON body 16 KiB; invalid or oversized input returns 400 CRASH_INVALID_INPUT.")]
    [ProducesResponseType<CrashGroupTriageDto>(200)]
    [ProducesResponseType<ProblemDetails>(404, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(409, "application/problem+json")]
    public async Task<IActionResult> UpdateCrashGroup(Guid id, [FromBody] UpdateCrashGroupRequest request, CancellationToken ct)
    {
        if (Request.Query.Count != 0)
            return CrashProblemMapper.Invalid();
        var patch = new CrashGroupPatch(request.TriageRevision,
            new FieldChange<CrashGroupStatus?>(request.HasStatus, CrashQuery.Status(request.Status)),
            new FieldChange<string>(request.HasFixedInVersion, request.FixedInVersion),
            new FieldChange<string>(request.HasIssueUrl, request.IssueUrl),
            new FieldChange<string>(request.HasNotes, request.Notes));
        CrashTriageResult result = await triage.UpdateAsync(new CrashGroupId(id), patch, timeProvider.GetUtcNow().ToUniversalTime(), ct);
        return CrashProblemMapper.Triage(result);
    }
}

internal static class CrashQuery
{
    public static bool IsAllowed(IQueryCollection query, params string[] allowed) => query.All(pair =>
        allowed.Contains(pair.Key, StringComparer.Ordinal) && pair.Value.Count == 1 && !string.IsNullOrWhiteSpace(pair.Value[0]));

    public static bool Page(string? value, int page, int max) => page >= 1 && page <= max
        && (value is null || int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed == page);

    public static CrashGroupStatus? Status(string? value) => value switch
    {
        "new" => CrashGroupStatus.New,
        "investigating" => CrashGroupStatus.Investigating,
        "fixed" => CrashGroupStatus.Fixed,
        "ignored" => CrashGroupStatus.Ignored,
        _ => null,
    };

    public static bool Since(string? value, DateTimeOffset now)
    {
        if (value is null) return true;
        string[] formats = ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"];
        if (!DateTimeOffset.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed) || parsed.Offset != TimeSpan.Zero || parsed > now)
        {
            return false;
        }
        return true;
    }
}
