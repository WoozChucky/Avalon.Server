namespace Avalon.Api.Hosting.Middlewares;

/// <summary>
/// How a service's own exceptions are answered (#794, design section 3.1). <see cref="ExceptionHandlerMiddleware"/>
/// asks every registered mapper, in registration order, before its shared mappings (the business, authentication,
/// database, cache and cancellation cases), so a service maps what only it throws.
/// </summary>
public interface IExceptionProblemMapper
{
    /// <summary>
    /// The answer to <paramref name="exception"/>, or null when this mapper does not map it. A mapper that maps it may
    /// first log it with <paramref name="logger"/>, the middleware's own.
    /// </summary>
    ExceptionProblem? Map(Exception exception, ILogger logger);
}

/// <summary>
/// The answer to a mapped exception: a ProblemDetails with this status, type, title and detail, or, for a
/// <see cref="ClientError"/>, the client-error ProblemDetails MVC writes for the status, as an endpoint's own
/// <c>NotFound()</c> or <c>Unauthorized()</c> does.
/// </summary>
public sealed record ExceptionProblem(int Status, string Type, string Title, string? Detail)
{
    /// <summary>Whether the answer is MVC's client-error ProblemDetails for <see cref="Status"/>.</summary>
    public bool IsClientError { get; private init; }

    /// <summary>MVC's client-error ProblemDetails for <paramref name="status"/>, as an endpoint's own answer.</summary>
    public static ExceptionProblem ClientError(int status) => new(status, "", "", null) { IsClientError = true };
}
