namespace Avalon.Api.Identity.Authentication;

/// <summary>
/// The game workload routes, under <c>/internal</c>, exist on the game workload listener only (#794, design D7.4). A
/// request for one of them is judged by the port its connection was accepted on, never by a header it carries: one
/// that arrived on any other port, the public listener behind the ingress included, is answered 404 whatever its
/// method and whatever host it names, before it is authenticated. On the workload listener nothing changes, and the
/// workload authentication (<see cref="GameServerAuthHandler"/>) still decides who may call them.
/// </summary>
public static class GameInternalRoutes
{
    /// <summary>Where the routes start, as the route manifest lists them internal-only.</summary>
    public const string Prefix = "/internal";

    /// <summary>
    /// Answers 404 to every request for <see cref="Prefix"/> or a path under it, without regard to case, that arrived on
    /// any port but <paramref name="port"/>.
    /// </summary>
    public static IApplicationBuilder UseGameInternalRoutes(this IApplicationBuilder app, int port) =>
        app.Use((context, next) =>
        {
            if (context.Connection.LocalPort == port
                || !context.Request.Path.StartsWithSegments(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return next(context);
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });
}
