using System.Text.Json;
using Avalon.Balance.Contract;
using Avalon.Balance.Core;
using Avalon.Balance.Service.Runs;

namespace Avalon.Balance.Service.Endpoints;

public static class RunEndpoints
{
    public static IEndpointRouteBuilder MapRunEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/runs", Post);
        routes.MapGet("/runs/{id}", Get);
        routes.MapDelete("/runs/{id}", Delete);
        return routes;
    }

    private static async Task<IResult> Post(HttpContext context, RunQueue queue)
    {
        RunRequestDto? dto;
        try
        {
            // An oversized body throws BadHttpRequestException (413) from here; Kestrel answers it.
            dto = await context.Request.ReadFromJsonAsync<RunRequestDto>(BalanceJson.Options, context.RequestAborted);
        }
        catch (JsonException)
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        if (dto is null)
            return Results.StatusCode(StatusCodes.Status400BadRequest);

        switch (queue.TryEnqueue(dto, out string runId, out IReadOnlyList<Issue> issues))
        {
            case EnqueueOutcome.Accepted:
                return Results.Json(new RunAcceptedDto(runId), BalanceJson.Options, statusCode: StatusCodes.Status202Accepted);
            case EnqueueOutcome.Full:
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            default:
                return Results.Json(
                    new RunStatusDto("", "invalid", 0, 0, null, issues.Select(i => new IssueDto(i.Path, i.Message)).ToList()),
                    BalanceJson.Options, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static IResult Get(string id, RunQueue queue) =>
        queue.Get(id) is { } record
            ? Results.Json(ToStatus(record), BalanceJson.Options)
            : Results.NotFound();

    private static IResult Delete(string id, RunQueue queue) =>
        queue.Cancel(id) ? Results.NoContent() : Results.NotFound();

    private static RunStatusDto ToStatus(RunRecord record)
    {
        // Status first: the result and issues are published before a finished status, so they are there if it is.
        RunState status = record.Status;
        (int done, int total) = record.Progress;
        return new RunStatusDto(
            record.Id,
            status.ToString().ToLowerInvariant(),
            done,
            total,
            status == RunState.Done ? record.Result : null,
            record.Issues);
    }
}
