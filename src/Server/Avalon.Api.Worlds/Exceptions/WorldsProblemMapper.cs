using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Worlds.Balance;

namespace Avalon.Api.Worlds.Exceptions;

/// <summary>The worlds service's exceptions (#794): characters, and the balance workbench's service.</summary>
public sealed class WorldsProblemMapper : IExceptionProblemMapper
{
    public ExceptionProblem? Map(Exception exception, ILogger logger)
    {
        switch (exception)
        {
            // A character deleted between the lookup and the write (#757): the endpoint's own NotFound(), not logged.
            case CharacterNotFoundException:
                return ExceptionProblem.ClientError(StatusCodes.Status404NotFound);
            // The balance workbench's service: not configured, or not reachable. Fixed wording; the
            // exception's inner cause stays in the log.
            case BalanceUnavailableException:
                logger.LogWarning(exception, "Balance service unavailable");
                return new ExceptionProblem(StatusCodes.Status503ServiceUnavailable, "ServiceUnavailable",
                    "Service unavailable", exception.Message);
            // A rename of a character that is in the world (#757): it can be renamed once logged out.
            case CharacterOnlineException:
                return new ExceptionProblem(StatusCodes.Status409Conflict, exception.GetType().Name, "Conflict",
                    exception.Message);
            default:
                return null;
        }
    }
}
