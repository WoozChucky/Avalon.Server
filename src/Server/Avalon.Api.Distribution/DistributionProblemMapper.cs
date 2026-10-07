using Avalon.Api.Hosting.Middlewares;

namespace Avalon.Api.Distribution;

/// <summary>
/// Distribution's exception (#794): no store configured, or a published build that is not there, answered 503 with
/// what is missing.
/// </summary>
public sealed class DistributionProblemMapper : IExceptionProblemMapper
{
    public ExceptionProblem? Map(Exception exception, ILogger logger) =>
        exception is DistributionUnavailableException
            ? new ExceptionProblem(StatusCodes.Status503ServiceUnavailable, "ServiceUnavailable", "Service unavailable",
                exception.Message)
            : null;
}
