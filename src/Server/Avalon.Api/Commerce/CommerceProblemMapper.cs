using Avalon.Api.Hosting.Middlewares;

namespace Avalon.Api.Commerce;

/// <summary>Commerce's exception (#794): a purchase refused, answered with its own status and code.</summary>
public sealed class CommerceProblemMapper : IExceptionProblemMapper
{
    public ExceptionProblem? Map(Exception exception, ILogger logger) =>
        exception is PurchaseException purchase
            ? new ExceptionProblem(purchase.StatusCode, purchase.Code, "Purchase unavailable", purchase.Message)
            : null;
}
