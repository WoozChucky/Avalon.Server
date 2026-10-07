using Avalon.Api.Hosting.Authentication;
using Avalon.Common.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Avalon.Api.Hosting.Worlds;

/// <summary>
/// Who a public (anonymous-friendly) request comes from: a signed-in caller's level (floored at Player), else a Player's.
/// A personal access token is looked up here, after the rate limiter, outside the FailedPatLookups budget.
/// </summary>
public static class PublicCaller
{
    public static async Task<AccountAccessLevel> AccessLevelAsync(HttpContext context)
    {
        IServiceProvider services = context.RequestServices;
        AuthorizationPolicy policy = await services.GetRequiredService<IAuthorizationPolicyProvider>().GetDefaultPolicyAsync();
        AuthenticateResult result = await services.GetRequiredService<IPolicyEvaluator>().AuthenticateAsync(policy, context);
        return result.Succeeded ? context.User.AccessLevel() | AccountAccessLevel.Player : AccountAccessLevel.Player;
    }
}
