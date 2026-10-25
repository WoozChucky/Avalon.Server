using System.Security.Claims;
using Avalon.Api.Hosting.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Avalon.Api.Identity.Crashes;

public static class CrashAdminPolicy
{
    public const string Name = "CrashAdminSession";

    // Keep both account-backed schemes: an authenticated PAT must be forbidden, not challenged.
    public static void Configure(AuthorizationOptions options) => options.AddPolicy(Name, policy => policy
        .Combine(options.DefaultPolicy)
        .RequireClaim(ClaimTypes.GroupSid, AvalonRoles.Admin)
        .RequireAssertion(context => !context.User.HasClaim(c => c.Type == "pat_id")
            && context.Resource is HttpContext http
            && http.Request.Headers.Authorization.Count == 1
            && http.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)));
}
