using System.Data.Common;
using Avalon.Crashes.Triage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Api.Identity.Crashes;

public static class CrashProblemMapper
{
    public static ObjectResult Invalid() => Problem(400, "CRASH_INVALID_INPUT", "Invalid crash API input.");
    public static ObjectResult Unavailable() => Problem(503, "CRASH_ANALYSIS_UNAVAILABLE", "Crash analysis is temporarily unavailable.");
    public static ObjectResult MissingGroup() => Problem(404, "CRASH_GROUP_NOT_FOUND", "Crash group was not found.");
    public static ObjectResult MissingReport() => Problem(404, "CRASH_REPORT_NOT_AVAILABLE", "This report has expired or is unavailable.");

    public static ObjectResult Problem(int status, string code, string title, long? currentRevision = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Type = "about:blank" };
        problem.Extensions["code"] = code;
        if (currentRevision is not null) problem.Extensions["currentRevision"] = currentRevision;
        var result = new ObjectResult(problem) { StatusCode = status };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    public static ObjectResult Triage(CrashTriageResult result) => result.Outcome switch
    {
        CrashTriageOutcome.Updated => new OkObjectResult(CrashDtoMapper.Triage(result.Group!)),
        CrashTriageOutcome.Missing => MissingGroup(),
        CrashTriageOutcome.Conflict => Problem(409, "CRASH_GROUP_CHANGED", "The crash group changed; reload before retrying.", result.CurrentRevision),
        _ => Invalid(),
    };

    public static bool IsUnavailable(Exception exception, CancellationToken ct) => exception is DbException or TimeoutException
        || exception is OperationCanceledException && !ct.IsCancellationRequested
        || exception is DbUpdateException { InnerException: { } inner } && IsUnavailable(inner, ct);
}
