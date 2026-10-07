using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Avalon.Api.Identity.Authentication;

public static class GameWorkloadAuthentication
{
    /// <summary>Establish the dedicated TLS principal before rate limiting; authorization repeats its cached result.</summary>
    public static IApplicationBuilder UseGameWorkloadAuthentication(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        bool workloadEndpoint = context.GetEndpoint()?.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(a => a.AuthenticationSchemes?.Split(',').Any(s => s.Trim() == GameServerAuthHandler.Scheme) == true) == true;
        if (workloadEndpoint)
        {
            AuthenticateResult result = await context.AuthenticateAsync(GameServerAuthHandler.Scheme);
            context.User = result.Succeeded ? result.Principal! : new System.Security.Claims.ClaimsPrincipal();
        }
        await next(context);
    });
}
