using System.Text.Json;
using Avalon.Balance.Contract;
using Avalon.Balance.Service.Export;
using Avalon.Balance.Service.Runs;
using Microsoft.Extensions.Options;

namespace Avalon.Balance.Service.Endpoints;

public static class ExportEndpoints
{
    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/exports", Post);
        return routes;
    }

    private static async Task<IResult> Post(
        HttpContext context,
        IOptions<BalanceServiceOptions> options,
        BuildInfo build,
        BalanceHost host,
        RunQueue queue,
        IGitHub github,
        TimeProvider time,
        ILoggerFactory loggers)
    {
        // Before anything is read or validated, so an unconfigured service never reaches GitHub.
        if (string.IsNullOrWhiteSpace(options.Value.GitHubToken))
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Exports are not configured",
                detail: "The service has no GitHub token (Balance:GitHubToken).");

        if (string.IsNullOrWhiteSpace(build.Commit))
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Exports are not available",
                detail: "The service has no build commit to branch from (it was built without a source revision).");

        ExportRequestDto? dto;
        try
        {
            dto = await context.Request.ReadFromJsonAsync<ExportRequestDto>(BalanceJson.Options, context.RequestAborted);
        }
        catch (JsonException)
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        if (dto is null)
            return Results.StatusCode(StatusCodes.Status400BadRequest);

        if (dto.Overrides is { } given && given.Count > options.Value.MaxOverrides)
            return Invalid([new IssueDto("overrides", $"overrides must have {options.Value.MaxOverrides} keys or fewer, not {given.Count}")]);

        // The result only while the run is done and still held; a finished record has dropped its request already.
        RunResultDto? run = dto.RunId is { } runId && queue.Get(runId) is { Status: RunState.Done } record ? record.Result : null;

        try
        {
            ExportResultDto result = await ExportComposer.ComposeAsync(dto, host, github, time, build.Commit, run, context.RequestAborted);
            return Results.Json(result, BalanceJson.Options);
        }
        catch (ExportInvalidException e)
        {
            return Invalid(e.Issues);
        }
        catch (ExportFailedException e)
        {
            // The message names the step and GitHub's status; the inner exception is not logged (it carries the request path only).
            loggers.CreateLogger("Avalon.Balance.Service.Export").LogWarning("Export failed: {Message}", e.Message);
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "GitHub did not accept the export",
                detail: e.Message);
        }
    }

    private static IResult Invalid(IReadOnlyList<IssueDto> issues) =>
        Results.Json(new RunStatusDto("", "invalid", 0, 0, null, issues), BalanceJson.Options, statusCode: StatusCodes.Status422UnprocessableEntity);
}
