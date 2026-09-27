using Avalon.Api.Authentication;
using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.Worlds;

/// <summary>
/// Selects the request's world on <see cref="WorldScopedAttribute"/> endpoints (#523). Runs between
/// authentication and authorization, so an unknown world is a 404 whatever the endpoint's role
/// policy, and that policy still decides everything else. In order:
/// <list type="number">
/// <item>No caller yet: passes through, and authorization answers 401 as everywhere.</item>
/// <item>A world id that is not in canonical form: 404, with nothing looked up.</item>
/// <item>No auth Worlds row for it, the caller fails its access rule, or it is not under
/// Database:Worlds: the same empty 404 each time. The row is read before the configuration is
/// asked, so an unconfigured world costs the same lookup as a restricted one, and a restricted
/// world is not revealed, even while it is unavailable.</item>
/// <item>Its databases failed at startup: 503.</item>
/// </list>
/// A personal access token is only authenticated by the authorization middleware (through the
/// default policy's schemes), so this authenticates the same way, explicitly; the handlers cache
/// their result, so the account is still loaded once per request.
/// </summary>
public sealed class WorldRouteMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<WorldScopedAttribute>() is null)
        {
            await next(context);
            return;
        }

        IServiceProvider services = context.RequestServices;
        AuthorizationPolicy policy = await services.GetRequiredService<IAuthorizationPolicyProvider>().GetDefaultPolicyAsync();
        AuthenticateResult authentication = await services.GetRequiredService<IPolicyEvaluator>().AuthenticateAsync(policy, context);
        if (!authentication.Succeeded)
        {
            await next(context);
            return;
        }

        if (!WorldDatabaseSettings.TryParseWorldId(context.Request.RouteValues[WorldScopedAttribute.RouteValue] as string,
                out WorldId? id))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        WorldEntity? world = await services.GetRequiredService<IWorldRepository>()
            .FindByIdAsync(id, track: false, context.RequestAborted);
        IWorldDatabases databases = services.GetRequiredService<IWorldDatabases>();
        if (world is null
            || !AccessLevels.ForWorld(world.AccessLevelRequired).Allows(context.User.AccessLevel())
            || !databases.TryGet(id, out _))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!databases.IsAvailable(id))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Type = "ServiceUnavailable",
                Title = "World unavailable",
                Detail = $"World {id.Value} is unavailable",
                Instance = $"{context.Request.Method} {context.Request.Path}",
            }, context.RequestAborted);
            return;
        }

        services.GetRequiredService<CurrentWorld>().Select(world.Id, world.Name);
        await next(context);
    }
}
