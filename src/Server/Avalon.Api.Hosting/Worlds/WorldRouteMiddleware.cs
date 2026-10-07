using Avalon.Api.Hosting.Authentication;
using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.Hosting.Worlds;

/// <summary>
/// Selects the request's world on <see cref="WorldScopedAttribute"/> endpoints (#523). Runs between
/// authentication and authorization, so an unknown world is a 404 whatever the endpoint's role
/// policy, and that policy still decides everything else. In order:
/// <list type="number">
/// <item>An endpoint that allows anonymous callers: the same empty 404. Authorization would let an
/// anonymous caller through to an action with no world selected, so it fails closed; no world
/// endpoint allows anonymous callers.</item>
/// <item>No caller yet: passes through, and authorization answers 401 as everywhere.</item>
/// <item>A world id that is not in canonical form: 404, with nothing looked up.</item>
/// <item>No auth Worlds row for it, the caller fails its access rule, or it is not under
/// Database:Worlds: the same empty 404 each time. The row is read before the configuration is
/// asked, so an unconfigured world costs the same lookup as a restricted one, and a restricted
/// world is not revealed, even while it is unavailable.</item>
/// <item>Its databases failed at startup: 503.</item>
/// </list>
/// <see cref="PublicWorldScopedAttribute"/> endpoints (/public/world/{worldId}/...) admit anonymous callers
/// as Players: anyone reads a world every player may enter, a signed-in caller also reads the worlds they
/// may enter, and everything else is the same empty 404 (503 when its databases failed).
/// A personal access token is only authenticated by the authorization middleware (through the
/// default policy's schemes), so this authenticates the same way, explicitly; the handlers cache
/// their result, so the account is still loaded once per request.
/// </summary>
public sealed class WorldRouteMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        Endpoint? endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<PublicWorldScopedAttribute>() is not null)
        {
            await SelectPublicWorldAsync(context);
            return;
        }

        if (endpoint?.Metadata.GetMetadata<WorldScopedAttribute>() is null)
        {
            await next(context);
            return;
        }

        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
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

        if (await TrySelectWorldAsync(context, context.User.AccessLevel()) is not null)
        {
            await next(context);
        }
    }

    private static async Task WriteUnavailableAsync(HttpContext context, WorldId id)
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
    }

    private async Task SelectPublicWorldAsync(HttpContext context)
    {
        AccountAccessLevel caller = await PublicCaller.AccessLevelAsync(context);
        WorldEntity? world = await TrySelectWorldAsync(context, caller);
        if (world is null)
        {
            return;
        }

        context.Items[PublicWorldScopedAttribute.OpenToEveryoneItem] =
            AccessLevels.ForWorld(world.AccessLevelRequired).Allows(AccountAccessLevel.Player);
        await next(context);
    }

    /// <summary>
    /// The one non-disclosure order: parse, auth row, access rule, configured, then available. Selects the
    /// world and returns it, or writes the 404 / 503 and returns null.
    /// </summary>
    private static async Task<WorldEntity?> TrySelectWorldAsync(HttpContext context, AccountAccessLevel caller)
    {
        IServiceProvider services = context.RequestServices;
        if (!WorldDatabaseSettings.TryParseWorldId(context.Request.RouteValues[WorldScopedAttribute.RouteValue] as string,
                out WorldId? id))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return null;
        }

        WorldEntity? world = await services.GetRequiredService<IWorldRepository>()
            .FindByIdAsync(id, track: false, context.RequestAborted);
        IWorldDatabases databases = services.GetRequiredService<IWorldDatabases>();
        if (world is null
            || !AccessLevels.ForWorld(world.AccessLevelRequired).Allows(caller)
            || !databases.TryGet(id, out _))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return null;
        }

        if (!databases.IsAvailable(id))
        {
            await WriteUnavailableAsync(context, id);
            return null;
        }

        services.GetRequiredService<CurrentWorld>().Select(world.Id, world.Name);
        return world;
    }
}
