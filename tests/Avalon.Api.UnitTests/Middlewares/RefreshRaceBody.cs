namespace Avalon.Api.UnitTests.Middlewares;

/// <summary>
/// #543: the 401 a refresh that lost a race is answered with, byte for byte: MVC's client-error
/// ProblemDetails for 401. The refresh endpoint gives it (<c>SessionIssuanceShould</c>), and the
/// exception middleware gives the same should the exception ever reach it
/// (<c>ExceptionHandlerMiddlewareMappingShould</c>).
/// </summary>
internal static class RefreshRaceBody
{
    public const string ContentType = "application/problem+json; charset=utf-8";

    public static string For(string traceId) =>
        "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.2\",\"title\":\"Unauthorized\",\"status\":401,\"traceId\":\""
        + traceId + "\"}";
}
