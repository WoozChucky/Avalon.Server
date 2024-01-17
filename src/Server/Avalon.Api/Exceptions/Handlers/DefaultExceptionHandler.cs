using System.Net;
using System.Security.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Exceptions.Handlers;

public class DefaultExceptionHandler(ILogger<DefaultExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "An unexpected error occurred");

        switch (exception)
        {
            case AuthenticationException ex:
                await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = (int)HttpStatusCode.Unauthorized,
                    Type = exception.GetType().Name,
                    Title = "Whoops!",
                    Detail = ex.Message,
                    Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
                }, cancellationToken: cancellationToken);
                return true;
            case BusinessException ex:
                await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = (int)HttpStatusCode.BadRequest,
                    Type = exception.GetType().Name,
                    Title = "Client error",
                    Detail = ex.Message,
                    Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
                }, cancellationToken: cancellationToken);
                return true;
        }

        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = (int)HttpStatusCode.InternalServerError,
            Type = exception.GetType().Name,
            Title = "An unexpected error occurred",
            Detail = exception.Message,
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
        }, cancellationToken: cancellationToken);

        return true;
    }
}
