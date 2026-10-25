using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Identity.Crashes;

public sealed class CrashFeatureGate(IOptions<CrashApiOptions> options, ICrashSchemaReadiness readiness) : IAsyncResourceFilter, IAsyncAlwaysRunResultFilter
{
    public const int MaxBodyBytes = 16 * 1024;

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is IStatusCodeActionResult { StatusCode: StatusCodes.Status415UnsupportedMediaType })
            context.Result = CrashProblemMapper.Invalid();
        await next();
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        if (!options.Value.Enabled)
        {
            context.Result = CrashProblemMapper.Problem(503, "CRASH_ANALYSIS_DISABLED", "Crash analysis is disabled.");
            return;
        }
        if (!await readiness.IsReadyAsync(context.HttpContext.RequestAborted))
        {
            context.Result = CrashProblemMapper.Unavailable();
            return;
        }
        HttpRequest request = context.HttpContext.Request;
        using var body = new MemoryStream();
        Stream original = request.Body;
        if (HttpMethods.IsPatch(request.Method))
        {
            if (!request.HasJsonContentType() || request.ContentLength > MaxBodyBytes)
            {
                context.Result = CrashProblemMapper.Invalid();
                return;
            }
            byte[] buffer = new byte[MaxBodyBytes + 1];
            int length = 0;
            while (length < buffer.Length)
            {
                int read = await original.ReadAsync(buffer.AsMemory(length), context.HttpContext.RequestAborted);
                if (read == 0) break;
                length += read;
            }
            if (length > MaxBodyBytes)
            {
                context.Result = CrashProblemMapper.Invalid();
                return;
            }
            await body.WriteAsync(buffer.AsMemory(0, length), context.HttpContext.RequestAborted);
            body.Position = 0;
            request.Body = body;
        }
        try
        {
            ResourceExecutedContext result = await next();
            if (result.Exception is { } exception && CrashProblemMapper.IsUnavailable(exception, request.HttpContext.RequestAborted))
            {
                result.ExceptionHandled = true;
                result.Result = CrashProblemMapper.Unavailable();
                await result.Result.ExecuteResultAsync(context);
            }
        }
        finally
        {
            request.Body = original;
        }
    }
}
