using Avalon.Api.Authentication;
using Avalon.Common.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Avalon.Api.Worlds;

/// <summary>Who a public (anonymous-friendly) request comes from: a signed-in caller's level, else a Player's.</summary>
public static class PublicCaller
{
    public static async Task<AccountAccessLevel> AccessLevelAsync(HttpContext context)
    {
        IServiceProvider services = context.RequestServices;
        AuthorizationPolicy policy = await services.GetRequiredService<IAuthorizationPolicyProvider>().GetDefaultPolicyAsync();
        AuthenticateResult result = await services.GetRequiredService<IPolicyEvaluator>().AuthenticateAsync(policy, context);
        return result.Succeeded ? context.User.AccessLevel() : AccountAccessLevel.Player;
    }
}
