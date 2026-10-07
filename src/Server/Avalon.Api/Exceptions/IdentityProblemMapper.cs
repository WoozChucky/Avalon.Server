using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Services.Email;

namespace Avalon.Api.Exceptions;

/// <summary>Identity's exceptions (#794): sign-in, refresh and email delivery.</summary>
public sealed class IdentityProblemMapper : IExceptionProblemMapper
{
    public ExceptionProblem? Map(Exception exception, ILogger logger) => exception switch
    {
        // A refresh that lost a race (#543). The refresh endpoint answers it itself; should it
        // ever get this far it is still that endpoint's 401, not a 500.
        RefreshAlreadyRotatedException => ExceptionProblem.ClientError(StatusCodes.Status401Unauthorized),
        // Only thrown once the caller has proved they hold the account (password or MFA code).
        AccountInactiveException => new ExceptionProblem(StatusCodes.Status403Forbidden, exception.GetType().Name,
            "Account not active", exception.Message),
        // A spent budget or a locked account (#478): the same answer for every username.
        AccountLockedException => new ExceptionProblem(StatusCodes.Status429TooManyRequests, exception.GetType().Name,
            "Too many attempts", exception.Message),
        // An email the request needed could not be sent (#510). The thrower logged it, by type
        // and domain only; this exception carries nothing more.
        EmailDeliveryException => new ExceptionProblem(StatusCodes.Status503ServiceUnavailable, "ServiceUnavailable",
            "Service unavailable", exception.Message),
        EmailVerificationUnavailableException => new ExceptionProblem(StatusCodes.Status501NotImplemented,
            "NotImplemented", "Email delivery unavailable", exception.Message),
        _ => null,
    };
}
